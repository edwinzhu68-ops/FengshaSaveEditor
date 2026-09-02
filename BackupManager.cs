using System.Text;
using System.Text.Json;

namespace FengshaSaveEditor;

internal sealed class BackupManifest
{
    public int Version { get; set; } = 1;
    public string CreatedAt { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string OriginalSlotPath { get; set; } = string.Empty;
    public string SlotName { get; set; } = string.Empty;
    public List<BackupFileEntry> Files { get; set; } = [];
}

internal sealed class BackupFileEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

/// <param name="Directory">本次新建的备份目录。</param>
/// <param name="Manifest">本次备份的清单。</param>
/// <param name="Pruned">本次因超出保留上限而被清掉的旧备份目录。</param>
internal sealed record BackupResult(string Directory, BackupManifest Manifest, List<string> Pruned);

internal static class BackupManager
{
    /// <summary>默认保留的备份份数，超出后从最旧的开始删。</summary>
    public const int MaxRetainedBackups = 10;

    /// <summary>保留份数之内也适用的总大小上限，超出后继续删最旧的。</summary>
    public const long MaxBackupTotalBytes = 30L * 1024 * 1024 * 1024;

    /// <summary>无论总量多大，至少保留这么多份。</summary>
    public const int MinRetainedBackups = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string GetBackupRootForSlot(string slotDirectory)
    {
        var slot = new DirectoryInfo(Path.GetFullPath(slotDirectory));
        var saveGames = slot.Parent ?? throw new InvalidDataException("无法确定存档根目录。");
        var saved = saveGames.Parent ?? throw new InvalidDataException("无法确定 Saved 目录。");
        return Path.Combine(saved.FullName, "FengshaSaveEditorBackups");
    }

    /// <param name="prune">是否在创建后裁剪旧备份。恢复流程创建的保护备份必须关掉，
    /// 否则可能把用户正在恢复的那个源备份一并清掉。</param>
    public static BackupResult Create(string slotDirectory, string reason, bool prune = true)
    {
        var slot = new DirectoryInfo(Path.GetFullPath(slotDirectory));
        if (!slot.Exists)
        {
            throw new DirectoryNotFoundException($"找不到存档槽：{slot.FullName}");
        }

        var root = GetBackupRootForSlot(slot.FullName);
        Directory.CreateDirectory(root);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeName = SanitizeName(slot.Name);
        var suffix = $"{safeName}_{timestamp}_{Guid.NewGuid():N}";
        var backupDirectory = Path.Combine(root, suffix[..Math.Min(90, suffix.Length)]);
        Directory.CreateDirectory(backupDirectory);

        var manifest = new BackupManifest
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Reason = reason,
            OriginalSlotPath = slot.FullName,
            SlotName = slot.Name
        };

        foreach (var sourcePath in Directory.EnumerateFiles(slot.FullName, "*", SearchOption.AllDirectories))
        {
            var sourceInfo = new FileInfo(sourcePath);
            if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"存档中包含不安全的链接文件，已停止备份：{sourcePath}");
            }

            var relative = Path.GetRelativePath(slot.FullName, sourcePath);
            var destination = GetSafeChildPath(backupDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: false);
            manifest.Files.Add(new BackupFileEntry
            {
                RelativePath = relative,
                Length = sourceInfo.Length,
                Sha256 = Hashing.Sha256File(sourcePath)
            });
        }

        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        Verify(backupDirectory, manifest);
        var pruned = prune ? Prune(slot.FullName, backupDirectory) : [];
        return new BackupResult(backupDirectory, manifest, pruned);
    }

    public static BackupManifest LoadAndVerify(string backupDirectory)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(backupDirectory), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("备份目录缺少 manifest.json，拒绝恢复。", manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("备份清单为空。");
        Verify(backupDirectory, manifest);
        return manifest;
    }

    public static void Verify(string backupDirectory, BackupManifest manifest)
    {
        var root = Path.GetFullPath(backupDirectory);
        foreach (var entry in manifest.Files)
        {
            var path = GetSafeChildPath(root, entry.RelativePath);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"备份文件缺失：{entry.RelativePath}");
            }

            var info = new FileInfo(path);
            if (info.Length != entry.Length || !string.Equals(Hashing.Sha256File(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"备份文件校验失败：{entry.RelativePath}");
            }
        }
    }

    /// <summary>
    /// 按数量与总大小裁剪旧备份。清单缺失或读取失败时按 0 字节计，不影响其余备份。
    /// </summary>
    public static List<string> Prune(string slotDirectory, params string[] protectedDirectories)
    {
        var backups = ListBackups(slotDirectory);
        if (backups.Count == 0)
        {
            return [];
        }

        var protect = protectedDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sized = new List<(string Directory, long Size)>();
        foreach (var directory in backups)
        {
            sized.Add((directory, ReadManifestSize(directory)));
        }

        // “恢复前自动保护”是恢复流程留下的最后安全垫，不能被普通保存操作的
        // 数量/容量裁剪顺手删除。它们仍会计入磁盘占用，用户可以通过备份页手动清理。
        foreach (var item in sized)
        {
            if (IsRestoreProtection(item.Directory))
            {
                protect.Add(Path.GetFullPath(item.Directory));
            }
        }

        var removed = new List<string>();
        for (var i = sized.Count - 1; i >= MaxRetainedBackups; i--)
        {
            TryDelete(sized[i].Directory, protect, removed);
        }

        var kept = sized.Take(Math.Min(MaxRetainedBackups, sized.Count)).ToList();
        var total = kept.Sum(item => item.Size);
        for (var i = kept.Count - 1; i >= MinRetainedBackups && total > MaxBackupTotalBytes; i--)
        {
            if (TryDelete(kept[i].Directory, protect, removed))
            {
                total -= kept[i].Size;
            }
        }

        return removed;
    }

    private static long ReadManifestSize(string backupDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(backupDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return 0;
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
            return manifest?.Files.Sum(entry => entry.Length) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsRestoreProtection(string backupDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(backupDirectory, "manifest.json");
            if (!File.Exists(manifestPath)) return false;
            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
            return manifest?.Reason.Contains("恢复前", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDelete(string directory, HashSet<string> protect, List<string> removed)
    {
        if (protect.Contains(Path.GetFullPath(directory)))
        {
            return false;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            removed.Add(directory);
            return true;
        }
        catch
        {
            // 备份被占用时先留着，下次再清，不影响本次写入。
            return false;
        }
    }

    /// <summary>
    /// 把备份目录的内容恢复到目标槽位。
    /// 备份清单之外的文件不会被删除：它们会被移到恢复前的保护备份目录里，
    /// 没有保护备份时原样保留并报告。
    /// </summary>
    public static string Restore(string backupDirectory, string? targetOverride)
    {
        var fullBackup = Path.GetFullPath(backupDirectory);
        var manifest = LoadAndVerify(fullBackup);
        var target = Path.GetFullPath(string.IsNullOrWhiteSpace(targetOverride) ? manifest.OriginalSlotPath : targetOverride!);
        var targetInfo = new DirectoryInfo(target);
        var parent = targetInfo.Parent ?? throw new InvalidDataException("恢复目标没有父目录。");
        Directory.CreateDirectory(parent.FullName);

        string? safetyBackup = null;
        if (targetInfo.Exists)
        {
            // 关掉裁剪：否则这次自动备份可能把正在恢复的源备份清掉。
            safetyBackup = Create(target, "恢复前自动保护", prune: false).Directory;
        }

        var stage = Path.Combine(parent.FullName, $".{targetInfo.Name}.fengsha-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var entry in manifest.Files)
            {
                var source = GetSafeChildPath(fullBackup, entry.RelativePath);
                var destination = GetSafeChildPath(stage, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }

            if (targetInfo.Exists)
            {
                var expected = manifest.Files
                    .Select(f => NormalizeRelative(f.RelativePath))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var current in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).ToList())
                {
                    var relative = NormalizeRelative(Path.GetRelativePath(target, current));
                    if (expected.Contains(relative))
                    {
                        continue;
                    }

                    if (safetyBackup is null)
                    {
                        // 目标目录原本不存在时不会有这种文件；真出现就原样保留并报告，绝不删除。
                        Console.Error.WriteLine($"[保留] 备份清单之外的文件，未删除：{current}");
                        continue;
                    }

                    var moved = GetSafeChildPath(safetyBackup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
                    File.Move(current, moved, overwrite: true);
                    Console.WriteLine($"[已移走] 备份清单之外的文件：{relative}");
                }
            }

            Directory.CreateDirectory(target);
            foreach (var staged in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stage, staged);
                var destination = GetSafeChildPath(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(staged, destination, overwrite: true);
            }
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }

        return safetyBackup ?? string.Empty;
    }

    public static List<string> ListBackups(string slotDirectory)
    {
        var root = GetBackupRootForSlot(slotDirectory);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var slotPath = Path.GetFullPath(slotDirectory);
        return Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
            .Where(d => BelongsToSlot(d, slotPath))
            .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
            .ToList();
    }

    private static bool BelongsToSlot(string backupDirectory, string slotPath)
    {
        try
        {
            var manifestPath = Path.Combine(backupDirectory, "manifest.json");
            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
            return manifest is not null
                && string.Equals(Path.GetFullPath(manifest.OriginalSlotPath), slotPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // 无法确认来源槽位的旧/损坏清单，不混入当前槽位列表。
            return false;
        }
    }

    private static string GetSafeChildPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"备份路径非法：{relative}");
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"备份路径越界：{relative}");
        }

        return fullPath;
    }

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.StartsWith(@".\", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
