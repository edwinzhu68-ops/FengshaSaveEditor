using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace FengshaSaveEditor;

internal sealed class AppOptions
{
    public string? SaveRoot { get; set; }
    public string? Slot { get; set; }
    public string? OodlePath { get; set; }
    public string? Restore { get; set; }
    public string? ResourceCategory { get; set; }
    public int? ResourceAmount { get; set; }
    public int? ResourceConfig { get; set; }
    public string? UnitType { get; set; }
    public int? UnitInstance { get; set; }
    public string? AttributeName { get; set; }
    public int? AttributeValue { get; set; }
    public string? PlayerAttributeName { get; set; }
    public int? PlayerValue { get; set; }
    public int Speed { get; set; } = 2000;
    public bool SpeedSpecified { get; set; }
    public bool Yes { get; set; }
    public bool DryRun { get; set; }
    public bool Verify { get; set; }
    public bool ListSlots { get; set; }
    public bool ListBackups { get; set; }
    public bool ListResources { get; set; }
    public bool ListAttributes { get; set; }
    public bool ListUnits { get; set; }
    public bool ListPlayerAttributes { get; set; }
    public bool ResourceLock { get; set; }
    public bool ScanRoads { get; set; }
    public bool Json { get; set; }
    public bool Help { get; set; }
}

internal sealed record LevelAnalysis(
    string Path,
    SaveContainer Container,
    GvasDocument Document,
    ResourceScanResult Scan,
    string FileSha256);

internal sealed record UnitMassAnalysis(
    string Path,
    SaveContainer Container,
    GvasDocument Document,
    UnitScanResult Scan,
    string FileSha256);

internal sealed record PlayerAnalysis(
    string Path,
    SaveContainer Container,
    GvasDocument Document,
    PlayerScanResult Scan,
    string FileSha256);

internal sealed record SlotInfo(string Path, string Name, DateTime LastActivity);

internal sealed record ResourcePatchTarget(int Capacity, int CurrentAmount);

internal static class Program
{
    private const int DefaultSpeed = 2000;
    private const int DefaultLockedResourceAmount = 9_999_999;
    private const int MaxResourceAmount = 1_000_000_000;
    private static readonly string[] RoadTokens =
    [
        "Road",
        "RoadLevel",
        "RoadBuff",
        "RoadSpeed",
        "RoadWidthAdjustRatio",
        "RoadCurvatureScale",
        "bIsRoadBuildingMod"
    ];

    private static readonly JsonSerializerOptions CliJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static string DefaultSaveRoot
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            }

            return Path.Combine(localAppData, "MOProject", "Saved", "SaveGames");
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new HeroEditorForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "烽沙 · 存档工坊", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 某些旧控制台不允许设置编码，不影响文件操作。
        }

        try
        {
            return RunCommand(ParseOptions(args));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"[未执行] {ex.Message}");
            return 1;
        }
    }

    private static AppOptions ParseOptions(string[] args)
    {
        var options = new AppOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    options.Help = true;
                    break;
                case "--save-root":
                    options.SaveRoot = RequireValue(args, ref i, arg);
                    break;
                case "--slot":
                    options.Slot = RequireValue(args, ref i, arg);
                    break;
                case "--oodle":
                    options.OodlePath = RequireValue(args, ref i, arg);
                    break;
                case "--resource":
                    options.ResourceCategory = RequireValue(args, ref i, arg);
                    break;
                case "--resource-amount":
                    if (!int.TryParse(RequireValue(args, ref i, arg), out var resourceAmount)
                        || resourceAmount <= 0
                        || resourceAmount > MaxResourceAmount)
                    {
                        throw new ArgumentException($"--resource-amount 必须是 1 到 {MaxResourceAmount} 之间的整数。");
                    }

                    options.ResourceAmount = resourceAmount;
                    break;
                case "--resource-config":
                    if (!int.TryParse(RequireValue(args, ref i, arg), out var resourceConfig) || resourceConfig < 0)
                    {
                        throw new ArgumentException("--resource-config 必须是非负整数。");
                    }

                    options.ResourceConfig = resourceConfig;
                    break;
                case "--resource-lock":
                    options.ResourceLock = true;
                    break;
                case "--unit":
                    options.UnitType = RequireValue(args, ref i, arg);
                    break;
                case "--unit-instance":
                    if (!int.TryParse(RequireValue(args, ref i, arg), out var unitInstance) || unitInstance < 0)
                    {
                        throw new ArgumentException("--unit-instance 必须是从 0 开始的非负整数。 ");
                    }

                    options.UnitInstance = unitInstance;
                    break;
                case "--attribute":
                case "--attr":
                    options.AttributeName = RequireValue(args, ref i, arg);
                    break;
                case "--value":
                    if (!int.TryParse(RequireValue(args, ref i, arg), out var attributeValue)
                        || attributeValue < -1_000_000_000
                        || attributeValue > 1_000_000_000)
                    {
                        throw new ArgumentException("--value 必须是 -1000000000 到 1000000000 之间的整数。");
                    }

                    options.AttributeValue = attributeValue;
                    break;
                case "--player-attribute":
                case "--player-attr":
                    options.PlayerAttributeName = RequireValue(args, ref i, arg);
                    break;
                case "--player-value":
                    if (!int.TryParse(RequireValue(args, ref i, arg), out var playerValue)
                        || playerValue < -1_000_000_000
                        || playerValue > 1_000_000_000)
                    {
                        throw new ArgumentException("--player-value 必须是 -1000000000 到 1000000000 之间的整数。");
                    }

                    options.PlayerValue = playerValue;
                    break;
                case "--speed":
                    if (!int.TryParse(RequireValue(args, ref i, arg), out var speed) || speed <= 0 || speed > 1_000_000)
                    {
                        throw new ArgumentException("--speed 必须是 1 到 1000000 之间的整数。");
                    }

                    options.Speed = speed;
                    options.SpeedSpecified = true;
                    break;
                case "--restore":
                    options.Restore = RequireValue(args, ref i, arg);
                    break;
                case "--yes":
                case "-y":
                    options.Yes = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--verify":
                    options.Verify = true;
                    break;
                case "--list":
                case "--list-slots":
                    options.ListSlots = true;
                    break;
                case "--list-backups":
                    options.ListBackups = true;
                    break;
                case "--list-resources":
                case "--resource-list":
                case "--scan-resources":
                    options.ListResources = true;
                    break;
                case "--list-attributes":
                case "--attribute-list":
                case "--list-unit-attributes":
                    options.ListAttributes = true;
                    break;
                case "--list-units":
                case "--unit-list":
                    options.ListUnits = true;
                    break;
                case "--list-player-attributes":
                case "--player-attribute-list":
                    options.ListPlayerAttributes = true;
                    break;
                case "--scan-roads":
                case "--road-scan":
                    options.ScanRoads = true;
                    break;
                case "--json":
                    options.Json = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"未知参数：{arg}。使用 --help 查看用法。");
                    }

                    options.Slot ??= arg;
                    break;
            }
        }

        var operationCount = (options.Restore is not null ? 1 : 0)
            + (options.Verify ? 1 : 0)
            + (options.ListSlots ? 1 : 0)
            + (options.ListBackups ? 1 : 0)
            + (options.ListResources ? 1 : 0)
            + (options.ListAttributes ? 1 : 0)
            + (options.ListUnits ? 1 : 0)
            + (options.ListPlayerAttributes ? 1 : 0)
            + (options.ScanRoads ? 1 : 0);
        if (operationCount > 1)
        {
            throw new ArgumentException("这些操作只能选择一个：--restore（会写入）、--verify、--list、--list-backups、--list-resources、--list-units、--list-attributes、--list-player-attributes、--scan-roads。");
        }

        if (options.DryRun && (options.Verify || options.Restore is not null || options.ListSlots || options.ListBackups || options.ListResources || options.ListUnits || options.ListAttributes || options.ListPlayerAttributes || options.ScanRoads))
        {
            throw new ArgumentException("--dry-run 只用于速度或资源修改预览。");
        }

        if (options.Json && !(options.ListUnits || options.ListAttributes || options.ListResources || options.ListPlayerAttributes))
        {
            throw new ArgumentException("--json 只用于单位、单位属性、资源或玩家属性的只读列表。");
        }

        if (options.ResourceAmount.HasValue && options.ResourceCategory is null)
        {
            throw new ArgumentException("--resource-amount 必须与 --resource 一起使用。");
        }

        if (options.ResourceConfig.HasValue && options.ResourceCategory is null)
        {
            throw new ArgumentException("--resource-config 必须与 --resource 一起使用。");
        }

        if (options.ResourceLock && options.ResourceCategory is null)
        {
            throw new ArgumentException("--resource-lock 必须与 --resource 一起使用。");
        }

        if (options.ResourceCategory is not null && !options.ResourceAmount.HasValue && !options.ResourceLock)
        {
            throw new ArgumentException("资源修改需要 --resource-amount N，或使用 --resource-lock 写入大储量。");
        }

        var genericAttributeOptionCount = (options.UnitType is not null ? 1 : 0)
            + (options.AttributeName is not null ? 1 : 0)
            + (options.AttributeValue.HasValue ? 1 : 0);
        if (!options.ListAttributes && genericAttributeOptionCount is > 0 and < 3)
        {
            throw new ArgumentException("单位属性修改需要同时指定 --unit、--attribute 和 --value。");
        }

        if (!options.ListAttributes && genericAttributeOptionCount > 0 && options.SpeedSpecified)
        {
            throw new ArgumentException("--speed 与 --unit/--attribute/--value 不能同时使用。");
        }

        if (!options.ListAttributes && genericAttributeOptionCount > 0 && options.ResourceCategory is not null)
        {
            throw new ArgumentException("单位属性修改与资源修改不能在一次命令中混用。");
        }

        if (options.SpeedSpecified && options.ResourceCategory is not null)
        {
            throw new ArgumentException("--speed 与资源修改不能在一次命令中混用。");
        }

        if (options.SpeedSpecified && options.ListAttributes)
        {
            throw new ArgumentException("--speed 与 --list-attributes 不能同时使用。");
        }

        if (options.ListAttributes && (options.AttributeName is not null || options.AttributeValue.HasValue))
        {
            throw new ArgumentException("--list-attributes 不需要 --attribute 或 --value；它会列出全部已确认属性。");
        }

        if (options.ListUnits && (options.UnitType is not null || options.UnitInstance.HasValue || options.AttributeName is not null || options.AttributeValue.HasValue))
        {
            throw new ArgumentException("--list-units 不需要单位筛选或属性参数。 ");
        }

        if (options.UnitInstance.HasValue && options.UnitType is null)
        {
            throw new ArgumentException("--unit-instance 必须与 --unit 一起使用。 ");
        }

        if ((options.PlayerAttributeName is null) != (!options.PlayerValue.HasValue))
        {
            throw new ArgumentException("玩家属性修改需要同时指定 --player-attribute 和 --player-value。");
        }

        if (options.PlayerAttributeName is not null
            && (genericAttributeOptionCount > 0 || options.ResourceCategory is not null || options.SpeedSpecified))
        {
            throw new ArgumentException("玩家属性修改不能与民夫/单位属性或资源修改混用。");
        }

        if (options.ListPlayerAttributes && (options.PlayerAttributeName is not null || options.PlayerValue.HasValue))
        {
            throw new ArgumentException("--list-player-attributes 不需要 --player-attribute 或 --player-value。");
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        // 只把「- 或 -- 后接字母」视作选项，否则 --value -100 这类负数值会被误判为缺少参数。
        var next = index + 1 < args.Length ? args[index + 1] : null;
        if (next is null || LooksLikeOption(next))
        {
            throw new ArgumentException($"{option} 缺少参数。");
        }

        index++;
        return args[index];
    }

    private static bool LooksLikeOption(string token)
    {
        return token.Length > 1 && token[0] == '-' && char.IsLetter(token[1]);
    }

    private static int RunCommand(AppOptions options)
    {
        if (options.Help)
        {
            PrintHelp();
            return 0;
        }

        var saveRoot = Path.GetFullPath(options.SaveRoot ?? DefaultSaveRoot);
        if (options.ListSlots)
        {
            PrintSlots(saveRoot);
            return 0;
        }

        if (options.ListBackups)
        {
            PrintBackups(saveRoot, options.Slot);
            return 0;
        }

        if (options.ListResources)
        {
            var resourceSlot = ResolveSlot(saveRoot, options.Slot);
            EnsureLevelSlot(resourceSlot);
            using var resourceOodle = LoadOodle(options.OodlePath, options.Json);
            if (options.Json)
            {
                PrintResourceAnalysisJson(AnalyzeLevelFile(Path.Combine(resourceSlot, "Level.sav"), resourceOodle));
            }
            else
            {
                PrintResourceAnalysis(AnalyzeLevelFile(Path.Combine(resourceSlot, "Level.sav"), resourceOodle), "资源点只读扫描");
            }
            return 0;
        }

        if (options.ListAttributes)
        {
            var attributeSlot = ResolveSlot(saveRoot, options.Slot);
            EnsureSlot(attributeSlot);
            using var attributeOodle = LoadOodle(options.OodlePath, options.Json);
            if (options.Json)
            {
                PrintUnitAttributesJson(AnalyzeUnitFile(Path.Combine(attributeSlot, "Mass.sav"), attributeOodle), options.UnitType);
            }
            else
            {
                PrintUnitAttributes(attributeSlot, options.UnitType, attributeOodle);
            }
            return 0;
        }

        if (options.ListUnits)
        {
            var unitSlot = ResolveSlot(saveRoot, options.Slot);
            EnsureSlot(unitSlot);
            using var unitOodle = LoadOodle(options.OodlePath, options.Json);
            if (options.Json)
            {
                PrintUnitTypesJson(AnalyzeUnitFile(Path.Combine(unitSlot, "Mass.sav"), unitOodle));
            }
            else
            {
                PrintUnitTypes(unitSlot, unitOodle);
            }
            return 0;
        }

        if (options.ListPlayerAttributes)
        {
            var playerSlot = ResolveSlot(saveRoot, options.Slot);
            EnsurePlayerSlot(playerSlot);
            using var playerOodle = LoadOodle(options.OodlePath, options.Json);
            if (options.Json)
            {
                PrintPlayerAttributesJson(AnalyzePlayerFile(Path.Combine(playerSlot, "Player.sav"), playerOodle));
            }
            else
            {
                PrintPlayerAttributes(playerSlot, playerOodle);
            }
            return 0;
        }

        if (options.Restore is not null)
        {
            return RestoreCommand(saveRoot, options);
        }

        if (options.Slot is null && !options.ScanRoads && !options.Verify)
        {
            options.Slot = FindSlots(saveRoot).FirstOrDefault()?.Name;
        }

        var slot = ResolveSlot(saveRoot, options.Slot);
        if (options.Verify)
        {
            VerifySlot(slot, options.OodlePath);
            return 0;
        }

        if (options.ScanRoads)
        {
            ScanRoads(slot, options.OodlePath);
            return 0;
        }

        if (options.ResourceCategory is not null)
        {
            return ModifyResourceSlot(
                slot,
                options.ResourceCategory,
                options.ResourceConfig,
                options.ResourceAmount,
                options.ResourceLock,
                options.OodlePath,
                options.Yes,
                options.DryRun);
        }

        if (options.AttributeName is not null)
        {
            return ModifyUnitSlot(
                slot,
                options.UnitType!,
                options.AttributeName,
                options.AttributeValue!.Value,
                options.UnitInstance,
                options.OodlePath,
                options.Yes,
                options.DryRun);
        }

        if (options.PlayerAttributeName is not null)
        {
            return ModifyPlayerSlot(
                slot,
                options.PlayerAttributeName,
                options.PlayerValue!.Value,
                options.OodlePath,
                options.Yes,
                options.DryRun);
        }

        return ModifySlot(slot, options.Speed, options.OodlePath, options.Yes, options.DryRun);
    }

    private static int ModifySlot(string slot, int speed, string? oodlePath, bool confirmed, bool dryRun)
    {
        // --speed 等价于「把民夫的 AT_MoveSpeed 改成指定值」，直接复用单位属性写入路径，
        // 不再维护 MinFuScanner 这一套独立的、校验更弱的扫描逻辑。
        return ModifyUnitSlot(slot, "MinFu", "AT_MoveSpeed", speed, oodlePath, confirmed, dryRun);
    }

    private static int ModifyResourceSlot(
        string slot,
        string category,
        int? configId,
        int? amount,
        bool lockMode,
        string? oodlePath,
        bool confirmed,
        bool dryRun)
    {
        EnsureLevelSlot(slot);
        var normalizedCategory = ResourceScanner.NormalizeCategory(category);
        var targetAmount = amount ?? (lockMode ? DefaultLockedResourceAmount : 0);
        if (targetAmount <= 0 || targetAmount > MaxResourceAmount)
        {
            throw new ArgumentException($"资源数量必须是 1 到 {MaxResourceAmount:N0} 之间的整数。");
        }

        using var oodle = LoadOodle(oodlePath);
        var analysis = AnalyzeLevelFile(Path.Combine(slot, "Level.sav"), oodle);
        PrintResourceAnalysis(analysis, "资源修改前扫描");

        var selected = analysis.Scan.Nodes
            .Where(node => normalizedCategory == "*"
                || string.Equals(node.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(node => !configId.HasValue || node.ConfigId == configId.Value)
            .ToList();
        if (selected.Count == 0)
        {
            var available = analysis.Scan.Nodes
                .GroupBy(node => node.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key)
                .Select(group => $"{ResourceScanner.GetCategoryLabel(group.Key)}({group.Key})")
                .ToList();
            throw new InvalidDataException(
                $"没有找到匹配的资源点：类别 {category}，ConfigID {(configId?.ToString() ?? "全部")}。当前可用类别：{string.Join("、", available)}。");
        }

        var safeAllMode = normalizedCategory == "*";
        var targets = selected.ToDictionary(
            node => node.RegionStart,
            node => safeAllMode
                ? new ResourcePatchTarget(node.CurrentCapacity, Math.Min(targetAmount, node.CurrentCapacity))
                : new ResourcePatchTarget(targetAmount, targetAmount));
        var unchanged = selected.All(node =>
        {
            var target = targets[node.RegionStart];
            return node.CurrentCapacity == target.Capacity && node.CurrentAmount == target.CurrentAmount;
        });
        var mode = lockMode ? "大储量/锁定模式" : "指定数量模式";
        var targetDescription = safeAllMode
            ? $"保留每个资源点原有最大容量，当前数量补至 {targetAmount:N0}（不超过各自上限）"
            : $"容量和当前数量设为 {targetAmount:N0}";
        Console.WriteLine($"目标：将 {selected.Count} 个{ResourceScanner.GetCategoryLabel(normalizedCategory)}资源点{targetDescription}（{mode}）。");
        if (unchanged)
        {
            Console.WriteLine("匹配的资源点已经是目标值，不需要重新压缩或写入。");
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine("预览模式：没有修改存档。");
            return 0;
        }

        if (!confirmed && !Confirm("确认修改这些资源点？"))
        {
            Console.WriteLine("已取消，没有写入。");
            return 0;
        }

        WarnIfGameRunning();
        Console.WriteLine("提示：工具不会自动备份，请在保存前自行备份整个存档槽。");

        var patchedRaw = (byte[])analysis.Document.Raw.Clone();
        foreach (var node in selected)
        {
            var target = targets[node.RegionStart];
            BinaryPrimitives.WriteInt32LittleEndian(
                patchedRaw.AsSpan(4 + node.CapacityValueOffset, 4), target.Capacity);
            BinaryPrimitives.WriteInt32LittleEndian(
                patchedRaw.AsSpan(4 + node.ItemValueOffset, 4), target.CurrentAmount);
        }

        var levelPath = Path.Combine(slot, "Level.sav");
        var tempPath = levelPath + $".fengsha-new-{Guid.NewGuid():N}.tmp";
        try
        {
            var candidate = analysis.Container.Recompress(patchedRaw, oodle);
            WriteDurable(tempPath, candidate);
            var candidateAnalysis = AnalyzeLevelFile(tempPath, oodle);
            ValidateResourceCandidate(analysis, candidateAnalysis, patchedRaw, selected, targets);
            Console.WriteLine($"候选文件校验通过：{candidate.Length:N0} 字节，CRC32 0x{candidateAnalysis.Container.ActualPayloadCrc:X8}。");

            WarnIfGameRunning();
            File.Move(tempPath, levelPath, overwrite: true);
            var finalAnalysis = AnalyzeLevelFile(levelPath, oodle);
            ValidateResourceCandidate(analysis, finalAnalysis, patchedRaw, selected, targets);
            Console.WriteLine();
            PrintResourceAnalysis(finalAnalysis, "写回后回读");
            Console.WriteLine("资源修改成功：匹配的全部资源点已写回，并通过完整解压回读。");
            if (safeAllMode)
            {
                Console.WriteLine("说明：全部资源模式会保留每种资源原有最大容量，仅把当前数量补到目标值；容量较小的资源点按自身上限处理，避免游戏加载异常。");
            }
            else if (lockMode)
            {
                Console.WriteLine("说明：这是把容量和当前数量写成固定大值；采集后游戏仍可能扣减。下次保存后再次运行即可重新补满。");
            }

            return 0;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static int ModifyUnitSlot(
        string slot,
        string unitType,
        string attribute,
        int targetValue,
        string? oodlePath,
        bool confirmed,
        bool dryRun)
    {
        return ModifyUnitSlot(slot, unitType, attribute, targetValue, null, oodlePath, confirmed, dryRun);
    }

    private static int ModifyUnitSlot(
        string slot,
        string unitType,
        string attribute,
        int targetValue,
        int? unitInstance,
        string? oodlePath,
        bool confirmed,
        bool dryRun)
    {
        EnsureSlot(slot);
        var normalizedUnit = UnitScanner.NormalizeUnit(unitType);
        var normalizedAttribute = UnitScanner.NormalizeAttribute(attribute);
        using var oodle = LoadOodle(oodlePath);
        var analysis = AnalyzeUnitFile(Path.Combine(slot, "Mass.sav"), oodle);
        var entries = UnitScanner.FindAttributeEntries(
            analysis.Document.Gvas,
            analysis.Scan,
            normalizedUnit,
            normalizedAttribute,
            unitInstance);
        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                $"没有找到可安全识别的单位属性：单位 {unitType}，属性 {attribute}。可用单位或属性可用 --list-attributes 查看。");
        }

        var unitRegionCount = entries.Select(entry => entry.Region.RegionStart).Distinct().Count();
        Console.WriteLine($"--- 单位属性修改前扫描 ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"SHA-256：{analysis.FileSha256}");
        Console.WriteLine($"单位区域：{analysis.Scan.Regions.Count:N0}；匹配单位区域：{unitRegionCount:N0}");
        Console.WriteLine($"作用范围：{(unitInstance.HasValue ? $"第 {unitInstance.Value + 1} 个匹配单位区域（该区域内全部字段）" : "全部匹配单位区域")}");
        Console.WriteLine($"目标属性：{UnitScanner.GetAttributeLabel(normalizedAttribute)} [{normalizedAttribute}]；匹配字段：{entries.Count:N0}");
        Console.WriteLine($"当前分布：{FormatDistribution(entries.Select(entry => entry.CurrentValue))}");

        if (entries.All(entry => entry.CurrentValue == targetValue))
        {
            Console.WriteLine("匹配的全部单位属性已经是目标值，不需要重新压缩或写入。");
            return 0;
        }

        Console.WriteLine($"目标：将匹配的全部 {entries.Count} 个字段设为 {targetValue}，不是只改一个单位。");
        if (dryRun)
        {
            Console.WriteLine("预览模式：没有修改存档。");
            return 0;
        }

        if (!confirmed && !Confirm("确认修改这些单位属性？"))
        {
            Console.WriteLine("已取消，没有写入。");
            return 0;
        }

        WarnIfGameRunning();
        Console.WriteLine("提示：工具不会自动备份，请在保存前自行备份整个存档槽。");

        var patchedRaw = (byte[])analysis.Document.Raw.Clone();
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                patchedRaw.AsSpan(4 + entry.ValueOffset, 4),
                targetValue);
        }

        var massPath = Path.Combine(slot, "Mass.sav");
        var tempPath = massPath + $".fengsha-new-{Guid.NewGuid():N}.tmp";
        try
        {
            var candidate = analysis.Container.Recompress(patchedRaw, oodle);
            WriteDurable(tempPath, candidate);
            var candidateAnalysis = AnalyzeUnitFile(tempPath, oodle);
            ValidateUnitCandidate(
                analysis,
                candidateAnalysis,
                patchedRaw,
                entries,
                normalizedUnit,
                normalizedAttribute,
                unitInstance,
                targetValue);
            Console.WriteLine($"候选文件校验通过：{candidate.Length:N0} 字节，CRC32 0x{candidateAnalysis.Container.ActualPayloadCrc:X8}。");

            WarnIfGameRunning();
            File.Move(tempPath, massPath, overwrite: true);
            var finalAnalysis = AnalyzeUnitFile(massPath, oodle);
            ValidateUnitCandidate(
                analysis,
                finalAnalysis,
                patchedRaw,
                entries,
                normalizedUnit,
                normalizedAttribute,
                unitInstance,
                targetValue);
            var finalEntries = UnitScanner.FindAttributeEntries(
                finalAnalysis.Document.Gvas,
                finalAnalysis.Scan,
                normalizedUnit,
                normalizedAttribute,
                unitInstance);
            Console.WriteLine();
            Console.WriteLine("--- 写回后回读 ---");
            Console.WriteLine($"文件：{finalAnalysis.Path}");
            Console.WriteLine($"SHA-256：{finalAnalysis.FileSha256}");
            Console.WriteLine($"VSOM CRC32：0x{finalAnalysis.Container.ActualPayloadCrc:X8}（有效）");
            Console.WriteLine($"回读分布：{FormatDistribution(finalEntries.Select(entry => entry.CurrentValue))}");
            Console.WriteLine("单位属性修改成功：匹配的全部字段已写回，并通过完整解压回读。");
            return 0;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static int ModifyPlayerSlot(
        string slot,
        string attribute,
        int targetValue,
        string? oodlePath,
        bool confirmed,
        bool dryRun)
    {
        EnsurePlayerSlot(slot);
        var normalizedAttribute = PlayerScanner.NormalizeAttribute(attribute);
        using var oodle = LoadOodle(oodlePath);
        var analysis = AnalyzePlayerFile(Path.Combine(slot, "Player.sav"), oodle);
        var entries = PlayerScanner.FindAttributeEntries(analysis.Scan, normalizedAttribute);
        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                $"没有找到可安全识别的玩家属性：{attribute}。可用属性请先用 --list-player-attributes 查看。");
        }

        Console.WriteLine("--- 玩家属性修改前扫描 ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"SHA-256：{analysis.FileSha256}");
        Console.WriteLine($"目标属性：{PlayerScanner.GetLabel(normalizedAttribute)} [{normalizedAttribute}]；匹配字段：{entries.Count:N0}");
        Console.WriteLine($"当前分布：{FormatDistribution(entries.Select(entry => entry.CurrentValue))}");

        if (entries.All(entry => entry.CurrentValue == targetValue))
        {
            Console.WriteLine("匹配的全部玩家属性已经是目标值，不需要重新压缩或写入。");
            return 0;
        }

        Console.WriteLine($"目标：将匹配的全部 {entries.Count} 个字段设为 {targetValue}。");
        if (dryRun)
        {
            Console.WriteLine("预览模式：没有修改存档。");
            return 0;
        }

        if (!confirmed && !Confirm("确认修改这个玩家属性？"))
        {
            Console.WriteLine("已取消，没有写入。");
            return 0;
        }

        WarnIfGameRunning();
        Console.WriteLine("提示：工具不会自动备份，请在保存前自行备份整个存档槽。");

        var patchedRaw = (byte[])analysis.Document.Raw.Clone();
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                patchedRaw.AsSpan(4 + entry.ValueOffset, 4),
                targetValue);
        }

        var playerPath = Path.Combine(slot, "Player.sav");
        var tempPath = playerPath + $".fengsha-new-{Guid.NewGuid():N}.tmp";
        try
        {
            var candidate = analysis.Container.Recompress(patchedRaw, oodle);
            WriteDurable(tempPath, candidate);
            var candidateAnalysis = AnalyzePlayerFile(tempPath, oodle);
            ValidatePlayerCandidate(analysis, candidateAnalysis, patchedRaw, entries, normalizedAttribute, targetValue);
            Console.WriteLine($"候选文件校验通过：{candidate.Length:N0} 字节，CRC32 0x{candidateAnalysis.Container.ActualPayloadCrc:X8}。");

            WarnIfGameRunning();
            File.Move(tempPath, playerPath, overwrite: true);
            var finalAnalysis = AnalyzePlayerFile(playerPath, oodle);
            ValidatePlayerCandidate(analysis, finalAnalysis, patchedRaw, entries, normalizedAttribute, targetValue);
            var finalEntries = PlayerScanner.FindAttributeEntries(finalAnalysis.Scan, normalizedAttribute);
            Console.WriteLine();
            Console.WriteLine("--- 写回后回读 ---");
            Console.WriteLine($"文件：{finalAnalysis.Path}");
            Console.WriteLine($"SHA-256：{finalAnalysis.FileSha256}");
            Console.WriteLine($"VSOM CRC32：0x{finalAnalysis.Container.ActualPayloadCrc:X8}（有效）");
            Console.WriteLine($"回读分布：{FormatDistribution(finalEntries.Select(entry => entry.CurrentValue))}");
            Console.WriteLine("玩家属性修改成功：匹配的全部字段已写回，并通过完整解压回读。");
            return 0;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static void ValidateResourceCandidate(
        LevelAnalysis original,
        LevelAnalysis candidate,
        byte[] expectedRaw,
        IReadOnlyList<ResourceNodeEntry> selected,
        IReadOnlyDictionary<int, ResourcePatchTarget> targets)
    {
        if (candidate.Scan.ResourceSaveIdFieldCount != original.Scan.ResourceSaveIdFieldCount
            || candidate.Scan.CandidateRecordCount != original.Scan.CandidateRecordCount
            || candidate.Scan.SkippedRecordCount != original.Scan.SkippedRecordCount
            || candidate.Scan.Nodes.Count != original.Scan.Nodes.Count)
        {
            throw new InvalidDataException("候选存档的资源点结构数量发生变化，拒绝写回。");
        }

        if (!candidate.Document.Raw.AsSpan().SequenceEqual(expectedRaw))
        {
            throw new InvalidDataException("候选存档解压后与预期数据不一致，拒绝写回。");
        }

        foreach (var expected in selected)
        {
            var target = targets[expected.RegionStart];
            var actual = candidate.Scan.Nodes.FirstOrDefault(node =>
                node.RegionStart == expected.RegionStart
                && node.Category.Equals(expected.Category, StringComparison.OrdinalIgnoreCase)
                && node.ConfigId == expected.ConfigId
                && node.ResourceSaveId == expected.ResourceSaveId);
            if (actual is null
                || actual.CurrentCapacity != target.Capacity
                || actual.CurrentAmount != target.CurrentAmount)
            {
                throw new InvalidDataException(
                    $"候选存档中资源点 0x{expected.RegionStart:X} 未达到安全目标数量，拒绝写回。");
            }
        }
    }

    private static void ValidateUnitCandidate(
        UnitMassAnalysis original,
        UnitMassAnalysis candidate,
        byte[] expectedRaw,
        IReadOnlyList<UnitAttributeEntry> selected,
        string unitType,
        string attribute,
        int? unitInstance,
        int targetValue)
    {
        if (candidate.Scan.SoldierTypeRegionCount != original.Scan.SoldierTypeRegionCount
            || candidate.Scan.Regions.Count != original.Scan.Regions.Count)
        {
            throw new InvalidDataException("候选存档的单位区域数量发生变化，拒绝写回。");
        }

        if (!candidate.Document.Raw.AsSpan().SequenceEqual(expectedRaw))
        {
            throw new InvalidDataException("候选存档解压后与预期数据不一致，拒绝写回。");
        }

        var candidateEntries = UnitScanner.FindAttributeEntries(
            candidate.Document.Gvas,
            candidate.Scan,
            unitType,
            attribute,
            unitInstance);
        if (candidateEntries.Count != selected.Count)
        {
            throw new InvalidDataException("候选存档的目标属性数量发生变化，拒绝写回。");
        }

        var selectedOffsets = selected.Select(entry => entry.ValueOffset).ToHashSet();
        if (!selectedOffsets.SetEquals(candidateEntries.Select(entry => entry.ValueOffset)))
        {
            throw new InvalidDataException("候选存档的目标属性位置发生变化，拒绝写回。");
        }

        foreach (var entry in candidateEntries)
        {
            if (selectedOffsets.Contains(entry.ValueOffset) && entry.CurrentValue != targetValue)
            {
                throw new InvalidDataException(
                    $"候选存档中 {UnitScanner.GetAttributeLabel(attribute)} 的字段未达到目标值，拒绝写回。");
            }
        }
    }

    private static void ValidatePlayerCandidate(
        PlayerAnalysis original,
        PlayerAnalysis candidate,
        byte[] expectedRaw,
        IReadOnlyList<PlayerAttributeEntry> selected,
        string attribute,
        int targetValue)
    {
        if (candidate.Scan.Entries.Count != original.Scan.Entries.Count)
        {
            throw new InvalidDataException("候选存档的玩家属性字段数量发生变化，拒绝写回。");
        }

        if (!candidate.Document.Raw.AsSpan().SequenceEqual(expectedRaw))
        {
            throw new InvalidDataException("候选存档解压后与预期数据不一致，拒绝写回。");
        }

        var candidateEntries = PlayerScanner.FindAttributeEntries(candidate.Scan, attribute);
        if (candidateEntries.Count != selected.Count
            || candidateEntries.Any(entry => entry.CurrentValue != targetValue))
        {
            throw new InvalidDataException(
                $"候选存档中玩家属性 {PlayerScanner.GetLabel(attribute)} 未全部达到目标值，拒绝写回。");
        }
    }

    private static void VerifySlot(string slot, string? oodlePath)
    {
        EnsureSlot(slot);
        using var oodle = LoadOodle(oodlePath);
        var analysis = AnalyzeUnitFile(Path.Combine(slot, "Mass.sav"), oodle);
        PrintUnitAnalysis(analysis, "只读校验");
        var levelAnalysis = AnalyzeLevelFile(Path.Combine(slot, "Level.sav"), oodle);
        PrintResourceAnalysis(levelAnalysis, "资源点只读校验");
        if (File.Exists(Path.Combine(slot, "Player.sav")))
        {
            var playerAnalysis = AnalyzePlayerFile(Path.Combine(slot, "Player.sav"), oodle);
            Console.WriteLine($"玩家全局字段：{playerAnalysis.Scan.Entries.Count:N0} 个，文件 CRC32 0x{playerAnalysis.Container.ActualPayloadCrc:X8}（有效）。");
        }

        Console.WriteLine("校验通过：VSOM CRC、所有 Oodle 分块、GVAS 长度/魔数、全部当前民夫、资源点和玩家字段均正常。");
    }

    private static void ScanRoads(string slot, string? oodlePath)
    {
        EnsureSlot(slot);
        using var oodle = LoadOodle(oodlePath);
        Console.WriteLine($"道路字段检测：{slot}");
        var anyKnownField = false;
        foreach (var fileName in new[] { "Level.sav", "Mass.sav", "Player.sav" })
        {
            var path = Path.Combine(slot, fileName);
            if (!File.Exists(path)) continue;
            var container = SaveContainer.Load(path);
            var raw = container.DecompressAll(oodle);
            var document = GvasDocument.Parse(raw);
            Console.WriteLine($"  {fileName}：{container.Blocks.Count} 块，解压 {raw.Length:N0} 字节");
            foreach (var token in RoadTokens)
            {
                var count = CountAscii(document.Gvas, token);
                if (count > 0)
                {
                    Console.WriteLine($"    {token}：{count} 处");
                    if (token is "RoadBuff" or "RoadSpeed") anyKnownField = true;
                }
            }
        }

        Console.WriteLine();
        if (anyKnownField)
        {
            Console.WriteLine("检测到道路数值字段名称，但当前工具仍不会猜测字段布局；需要单独确认三种道路的数值位置后再加入写回功能。");
        }
        else
        {
            Console.WriteLine("结论：存档里可能有 Road 记录名称，但没有发现可安全关联土路/夯土路/石板路加成的字段；道路加成不在本工具的存档写回范围内。");
        }
        Console.WriteLine("本次检测为只读，没有改动任何文件。");
    }

    private static int RestoreCommand(string saveRoot, AppOptions options)
    {
        var backup = ResolveBackup(saveRoot, options.Restore!);
        var target = options.Slot is null ? null : ResolveSlot(saveRoot, options.Slot);
        Console.WriteLine($"待恢复备份：{backup}");
        var manifest = BackupManager.LoadAndVerify(backup);
        Console.WriteLine($"来源槽位：{manifest.OriginalSlotPath}");
        var targetPath = target ?? manifest.OriginalSlotPath;
        Console.WriteLine($"恢复目标：{targetPath}");
        if (!options.Yes && !Confirm("确认恢复？恢复前会自动再备份当前槽位。"))
        {
            Console.WriteLine("已取消，没有写入。");
            return 0;
        }

        WarnIfGameRunning();
        var safety = BackupManager.Restore(backup, target);
        Console.WriteLine("恢复完成，备份内容已通过逐文件 SHA-256 校验。");
        if (!string.IsNullOrEmpty(safety)) Console.WriteLine($"恢复前的当前槽位保护备份：{safety}");
        return 0;
    }

    private static LevelAnalysis AnalyzeLevelFile(string path, OodleNative oodle)
    {
        var container = SaveContainer.Load(path);
        var raw = container.DecompressAll(oodle);
        var document = GvasDocument.Parse(raw);
        var scan = ResourceScanner.Scan(document.Gvas);
        return new LevelAnalysis(path, container, document, scan, Hashing.Sha256File(path));
    }

    private static UnitMassAnalysis AnalyzeUnitFile(string path, OodleNative oodle)
    {
        var container = SaveContainer.Load(path);
        var raw = container.DecompressAll(oodle);
        var document = GvasDocument.Parse(raw);
        var scan = UnitScanner.Scan(document.Gvas);
        return new UnitMassAnalysis(path, container, document, scan, Hashing.Sha256File(path));
    }

    private static PlayerAnalysis AnalyzePlayerFile(string path, OodleNative oodle)
    {
        var container = SaveContainer.Load(path);
        var raw = container.DecompressAll(oodle);
        var document = GvasDocument.Parse(raw);
        var scan = PlayerScanner.Scan(document.Gvas);
        return new PlayerAnalysis(path, container, document, scan, Hashing.Sha256File(path));
    }

    private static void PrintUnitAnalysis(UnitMassAnalysis analysis, string title)
    {
        var minFuRegions = analysis.Scan.Regions.Count(region => region.UnitType == "MinFu");
        Console.WriteLine($"--- {title} ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"SHA-256：{analysis.FileSha256}");
        Console.WriteLine($"大小：{analysis.Container.FileSize:N0} 字节；Oodle 分块：{analysis.Container.Blocks.Count}；解压：{analysis.Container.UncompressedTotal:N0} 字节");
        Console.WriteLine($"VSOM CRC32：0x{analysis.Container.ActualPayloadCrc:X8}（有效）");
        Console.WriteLine($"GVAS 声明长度：{analysis.Document.DeclaredLength:N0}；SoldierTypeCommonSaveData：{analysis.Scan.SoldierTypeRegionCount:N0} 个区域");
        Console.WriteLine($"可识别单位区域：{analysis.Scan.Regions.Count:N0}；其中民夫区域：{minFuRegions:N0}");

        // 只读校验不因为某一项扫不到就整体中断，把原因原样报出来。
        try
        {
            var speedEntries = UnitScanner.FindAttributeEntries(
                analysis.Document.Gvas, analysis.Scan, "MinFu", "AT_MoveSpeed");
            Console.WriteLine($"民夫 AT_MoveSpeed 字段：{speedEntries.Count:N0}");
            Console.WriteLine($"当前速度分布：{FormatDistribution(speedEntries.Select(entry => entry.CurrentValue))}");
        }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"民夫 AT_MoveSpeed：{ex.Message}");
        }
    }
    private static void PrintResourceAnalysis(LevelAnalysis analysis, string title)
    {
        Console.WriteLine($"--- {title} ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"SHA-256：{analysis.FileSha256}");
        Console.WriteLine($"大小：{analysis.Container.FileSize:N0} 字节；Oodle 分块：{analysis.Container.Blocks.Count}；解压：{analysis.Container.UncompressedTotal:N0} 字节");
        Console.WriteLine($"VSOM CRC32：0x{analysis.Container.ActualPayloadCrc:X8}（有效）");
        Console.WriteLine($"ResourceSaveID 字段：{analysis.Scan.ResourceSaveIdFieldCount:N0}；候选记录：{analysis.Scan.CandidateRecordCount:N0}；安全识别资源点：{analysis.Scan.Nodes.Count:N0}；跳过：{analysis.Scan.SkippedRecordCount:N0}");

        foreach (var group in analysis.Scan.Nodes
                     .GroupBy(node => new { node.Category, node.ConfigId })
                     .OrderBy(group => group.Key.Category, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(group => group.Key.ConfigId))
        {
            var label = ResourceScanner.GetCategoryLabel(group.Key.Category);
            Console.WriteLine(
                $"  {label} [{group.Key.Category}] ConfigID={group.Key.ConfigId}：{group.Count()} 个；容量 {FormatDistribution(group.Select(node => node.CurrentCapacity))}；当前 {FormatDistribution(group.Select(node => node.CurrentAmount))}");
        }
    }

    private static void PrintUnitAttributes(string slot, string? unitType, OodleNative oodle)
    {
        EnsureSlot(slot);
        var analysis = AnalyzeUnitFile(Path.Combine(slot, "Mass.sav"), oodle);
        var normalizedUnit = string.IsNullOrWhiteSpace(unitType) ? "*" : UnitScanner.NormalizeUnit(unitType);
        var matchingRegions = analysis.Scan.Regions
            .Where(region => UnitScanner.MatchesUnit(region.UnitType, normalizedUnit))
            .ToList();

        Console.WriteLine("--- 已确认可修改的单位属性 ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"单位区域：{analysis.Scan.Regions.Count:N0}；筛选区域：{matchingRegions.Count:N0}；筛选：{(normalizedUnit == "*" ? "全部单位" : UnitScanner.GetUnitLabel(normalizedUnit))}");
        Console.WriteLine("注意：数值按存档原始整数写入；百分比/倍率字段请先查看当前分布再改。");

        foreach (var attribute in UnitScanner.SupportedAttributes.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            List<UnitAttributeEntry> entries;
            try
            {
                entries = UnitScanner.FindAttributeEntries(analysis.Document.Gvas, analysis.Scan, normalizedUnit, attribute);
            }
            catch (InvalidDataException)
            {
                // 列表命令只展示当前存档中存在且通过布局校验的属性；
                // 某个已知属性没出现在当前单位上，不应让整个只读列表失败。
                continue;
            }

            if (entries.Count == 0)
            {
                continue;
            }

            Console.WriteLine(
                $"  {UnitScanner.GetAttributeLabel(attribute),-12} [{attribute}]：{entries.Count:N0} 个字段；当前 {FormatDistribution(entries.Select(entry => entry.CurrentValue))}");
        }
    }

    private static void PrintUnitTypes(string slot, OodleNative oodle)
    {
        EnsureSlot(slot);
        var analysis = AnalyzeUnitFile(Path.Combine(slot, "Mass.sav"), oodle);
        Console.WriteLine("--- 当前存档单位类型 ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"已识别单位区域：{analysis.Scan.Regions.Count:N0}");
        foreach (var group in analysis.Scan.Regions
                     .GroupBy(region => region.UnitType, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {UnitScanner.GetUnitLabel(group.Key)} [{group.Key}]：{group.Count():N0} 个单位区域");
        }

        var present = analysis.Scan.Regions
            .Select(region => region.UnitType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var absent = UnitScanner.KnownUnitTypes
            .Where(type => !present.Contains(type))
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (absent.Count > 0)
        {
            Console.WriteLine("当前存档未发现但配置中已知的单位模板：");
            foreach (var type in absent)
            {
                Console.WriteLine($"  {UnitScanner.GetUnitLabel(type)} [{type}]：当前存档未发现");
            }
        }
    }

    private static void PrintPlayerAttributes(string slot, OodleNative oodle)
    {
        EnsurePlayerSlot(slot);
        var analysis = AnalyzePlayerFile(Path.Combine(slot, "Player.sav"), oodle);
        Console.WriteLine("--- 已确认可修改的玩家全局属性 ---");
        Console.WriteLine($"文件：{analysis.Path}");
        Console.WriteLine($"SHA-256：{analysis.FileSha256}");
        Console.WriteLine($"已识别 AT_ 字段：{analysis.Scan.Entries.Count:N0} 个，属性种类：{analysis.Scan.AttributeNames.Count:N0}");
        Console.WriteLine("这些是玩家/全局参数；同一属性出现多次时工具会全部改动。");

        foreach (var attribute in analysis.Scan.AttributeNames)
        {
            var entries = PlayerScanner.FindAttributeEntries(analysis.Scan, attribute);
            Console.WriteLine(
                $"  {PlayerScanner.GetLabel(attribute),-16} [{attribute}]：{entries.Count:N0} 个字段；当前 {FormatDistribution(entries.Select(entry => entry.CurrentValue))}");
        }
    }

    private static void PrintUnitTypesJson(UnitMassAnalysis analysis)
    {
        var counts = analysis.Scan.Regions
            .GroupBy(region => region.UnitType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var keys = UnitScanner.KnownUnitTypes
            .Concat(counts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => UnitScanner.GetUnitLabel(key), StringComparer.Ordinal)
            .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => new UnitListItem(
                key,
                UnitScanner.GetUnitLabel(key),
                counts.TryGetValue(key, out var count) ? count : 0))
            .ToList();

        PrintJson(new UnitListResponse(analysis.Scan.Regions.Count, keys));
    }

    private static void PrintUnitAttributesJson(UnitMassAnalysis analysis, string? unitType)
    {
        var normalizedUnit = string.IsNullOrWhiteSpace(unitType) ? "*" : UnitScanner.NormalizeUnit(unitType);
        var attributes = new List<AttributeListItem>();
        foreach (var attribute in UnitScanner.SupportedAttributes.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var entries = UnitScanner.FindAttributeEntries(
                    analysis.Document.Gvas,
                    analysis.Scan,
                    normalizedUnit,
                    attribute);
                if (entries.Count > 0)
                {
                    attributes.Add(new AttributeListItem(
                        attribute,
                        UnitScanner.GetAttributeLabel(attribute),
                        entries.Count,
                        FormatDistribution(entries.Select(entry => entry.CurrentValue))));
                }
            }
            catch (InvalidDataException)
            {
                // 该属性在当前单位上不存在时跳过，不让目录列表失败。
            }
        }

        PrintJson(new AttributeListResponse(
            normalizedUnit,
            analysis.Scan.Regions.Count,
            attributes));
    }

    private static void PrintResourceAnalysisJson(LevelAnalysis analysis)
    {
        var groups = analysis.Scan.Nodes
            .GroupBy(node => new { node.Category, node.ConfigId })
            .OrderBy(group => group.Key.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.ConfigId)
            .ToList();
        var capacitiesByCategory = analysis.Scan.Nodes
            .GroupBy(node => node.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(node => node.CurrentCapacity),
                StringComparer.OrdinalIgnoreCase);
        var rows = groups
            .Select(group =>
            {
                var capacities = group.Select(node => node.CurrentCapacity).Distinct().ToList();
                var capacity = capacities.FirstOrDefault();
                var currentValues = group.Select(node => node.CurrentAmount).Distinct().ToList();
                var currentAmount = currentValues.Count == 1 ? currentValues[0] : (int?)null;
                return new ResourceListItem(
                    ResourceScanner.GetCategoryLabel(group.Key.Category),
                    group.Key.Category,
                    group.Key.ConfigId,
                    ResourceScanner.GetSizeLabel(capacitiesByCategory[group.Key.Category], capacity),
                    group.Count(),
                    capacity,
                    currentAmount,
                    $"容量 {FormatDistribution(group.Select(node => node.CurrentCapacity))}；当前 {FormatDistribution(group.Select(node => node.CurrentAmount))}");
            })
            .ToList();

        PrintJson(new ResourceListResponse(
            analysis.Scan.ResourceSaveIdFieldCount,
            analysis.Scan.CandidateRecordCount,
            analysis.Scan.SkippedRecordCount,
            rows));
    }

    private static void PrintPlayerAttributesJson(PlayerAnalysis analysis)
    {
        var rows = analysis.Scan.AttributeNames
            .Select(attribute =>
            {
                var entries = PlayerScanner.FindAttributeEntries(analysis.Scan, attribute);
                return new PlayerListItem(
                    attribute,
                    PlayerScanner.GetLabel(attribute),
                    entries.Count,
                    FormatDistribution(entries.Select(entry => entry.CurrentValue)));
            })
            .ToList();

        PrintJson(new PlayerListResponse(
            analysis.Scan.Entries.Count,
            rows.Count,
            rows));
    }

    private static void PrintJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, CliJsonOptions));
    }

    private static string FormatDistribution(IEnumerable<int> values)
    {
        var groups = values.GroupBy(v => v).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}").ToList();
        return groups.Count == 0 ? "无" : string.Join("，", groups);
    }

    private static void PrintSlots(string saveRoot)
    {
        var slots = FindSlots(saveRoot);
        Console.WriteLine($"存档根目录：{saveRoot}");
        if (slots.Count == 0)
        {
            Console.WriteLine("没有找到同时包含 Mass.sav 和 Level.sav 的槽位。");
            return;
        }

        for (var i = 0; i < slots.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {slots[i].Name}    最近修改：{slots[i].LastActivity:yyyy-MM-dd HH:mm:ss}");
        }
    }

    private static void PrintBackups(string saveRoot, string? slotName)
    {
        var baseSlot = slotName is null
            ? FindSlots(saveRoot).FirstOrDefault()?.Path
            : ResolveSlot(saveRoot, slotName);
        if (baseSlot is null)
        {
            Console.WriteLine("没有找到槽位，因此无法确定备份目录。");
            return;
        }

        var backups = BackupManager.ListBackups(baseSlot);
        Console.WriteLine($"备份目录：{BackupManager.GetBackupRootForSlot(baseSlot)}");
        if (backups.Count == 0)
        {
            Console.WriteLine("没有找到备份。");
            return;
        }

        PrintBackupList(backups);
    }

    private static void PrintBackupList(IReadOnlyList<string> backups)
    {
        for (var i = 0; i < backups.Count; i++)
        {
            try
            {
                var manifest = BackupManager.LoadAndVerify(backups[i]);
                Console.WriteLine($"{i + 1}. {Path.GetFileName(backups[i])}    槽位：{manifest.SlotName}    文件：{manifest.Files.Count}    校验：通过");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{i + 1}. {Path.GetFileName(backups[i])}    校验：失败（{ex.Message}）");
            }
        }
    }

    private static string? SelectSlot(string saveRoot)
    {
        var slots = FindSlots(saveRoot);
        if (slots.Count == 0)
        {
            Console.WriteLine("没有找到有效存档槽。");
            return null;
        }

        PrintSlots(saveRoot);
        Console.Write("选择编号（直接回车使用最近修改的槽位）：");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return slots[0].Path;
        if (int.TryParse(input, out var index) && index >= 1 && index <= slots.Count) return slots[index - 1].Path;
        Console.WriteLine("编号无效。");
        return null;
    }

    private static List<SlotInfo> FindSlots(string saveRoot)
    {
        if (!Directory.Exists(saveRoot)) return [];
        return Directory.EnumerateDirectories(saveRoot)
            .Where(d => File.Exists(Path.Combine(d, "Mass.sav")) && File.Exists(Path.Combine(d, "Level.sav")))
            .Select(d => new SlotInfo(d, Path.GetFileName(d), GetLastActivity(d)))
            .OrderByDescending(s => s.LastActivity)
            .ToList();
    }

    private static DateTime GetLastActivity(string slot)
    {
        var dates = Directory.EnumerateFiles(slot, "*", SearchOption.TopDirectoryOnly)
            .Select(File.GetLastWriteTime)
            .ToList();
        return dates.Count == 0 ? Directory.GetLastWriteTime(slot) : dates.Max();
    }

    private static string ResolveSlot(string saveRoot, string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            var first = FindSlots(saveRoot).FirstOrDefault()
                ?? throw new DirectoryNotFoundException($"在 {saveRoot} 中没有找到有效存档槽。");
            return first.Path;
        }

        var path = Path.IsPathRooted(slot) ? Path.GetFullPath(slot) : Path.GetFullPath(Path.Combine(saveRoot, slot));
        return path;
    }

    private static string ResolveBackup(string saveRoot, string backup)
    {
        if (Directory.Exists(backup)) return Path.GetFullPath(backup);
        var baseSlot = FindSlots(saveRoot).FirstOrDefault()?.Path
            ?? throw new DirectoryNotFoundException("没有槽位，无法定位备份目录。");
        var candidate = Path.Combine(BackupManager.GetBackupRootForSlot(baseSlot), backup);
        if (!Directory.Exists(candidate)) throw new DirectoryNotFoundException($"找不到备份：{backup}");
        return candidate;
    }

    private static void EnsureSlot(string slot)
    {
        if (!Directory.Exists(slot) || !File.Exists(Path.Combine(slot, "Mass.sav")))
        {
            throw new FileNotFoundException($"存档槽缺少 Mass.sav：{slot}");
        }
    }

    private static void EnsureLevelSlot(string slot)
    {
        if (!Directory.Exists(slot) || !File.Exists(Path.Combine(slot, "Level.sav")))
        {
            throw new FileNotFoundException($"存档槽缺少 Level.sav：{slot}");
        }
    }

    private static void EnsurePlayerSlot(string slot)
    {
        if (!Directory.Exists(slot) || !File.Exists(Path.Combine(slot, "Player.sav")))
        {
            throw new FileNotFoundException($"存档槽缺少 Player.sav：{slot}");
        }
    }

    private static OodleNative LoadOodle(string? requested, bool quiet = false)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requested)) candidates.Add(requested);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "oo2core_9_win64.dll"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "oo2core_9_win64.dll"));
        var path = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        if (path is null)
        {
            throw new FileNotFoundException("没有找到 oo2core_9_win64.dll。请把它放在 EXE 同目录，或使用 --oodle 指定路径。");
        }

        if (!quiet) Console.WriteLine($"加载 Oodle：{path}");
        return OodleNative.Load(path);
    }

    private static void WarnIfGameRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("MOProject-Win64-Shipping");
        if (processes.Length > 0)
        {
            var ids = string.Join(", ", processes.Select(p => p.Id));
            foreach (var process in processes) process.Dispose();
            Console.Error.WriteLine($"警告：游戏仍在运行（PID {ids}），继续执行文件级写回；如果游戏此刻保存，可能覆盖本次修改。");
            return;
        }
    }

    private static bool IsGameRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("MOProject-Win64-Shipping");
        var running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        return running;
    }

    private static void WriteDurable(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough);
        stream.Write(data, 0, data.Length);
        stream.Flush(flushToDisk: true);
    }

    private static int CountAscii(byte[] data, string value)
    {
        var pattern = Encoding.ASCII.GetBytes(value);
        var count = 0;
        var position = 0;
        while (position <= data.Length - pattern.Length)
        {
            var relative = data.AsSpan(position).IndexOf(pattern);
            if (relative < 0) break;
            count++;
            position += relative + 1;
        }

        return count;
    }

    private static bool Confirm(string message)
    {
        Console.Write($"{message} [y/N] ");
        var text = Console.ReadLine()?.Trim().ToLowerInvariant();
        return text is "y" or "yes" or "是" or "确认";
    }

    private static void PrintHelp()
    {
        Console.WriteLine("烽沙存档修改器");
        Console.WriteLine();
        Console.WriteLine("双击 FengshaSaveEditor.exe 可进入图形界面。游戏运行中也可写入，但请避免游戏同时保存。");
        Console.WriteLine();
        Console.WriteLine("常用命令：");
        Console.WriteLine("  FengshaSaveEditor.exe --list");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --speed 2000");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --speed 2000 --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --dry-run --speed 2500");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --resource IronOre --resource-amount 9999999");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --resource 枣子林 --resource-lock --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --resource all --resource-amount 99999 --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --resource 铁矿 --resource-config 33536 --resource-amount 9999999");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --list-resources");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 攻击 --value 999 --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 生命 --value 1000 --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --unit-instance 0 --attribute 移速 --value 2000 --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --list-units");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --list-attributes --unit 民夫");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --player-attribute 搬运容量 --player-value 100 --yes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --list-player-attributes");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --list-units --json");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --verify");
        Console.WriteLine("  FengshaSaveEditor.exe --slot 新存档_3 --scan-roads");
        Console.WriteLine("  FengshaSaveEditor.exe --list-backups");
        Console.WriteLine("  FengshaSaveEditor.exe --restore <备份目录> [--slot 槽位] --yes");
        Console.WriteLine();
        Console.WriteLine("参数：");
        Console.WriteLine("  --speed N       把当前槽位全部已存在民夫速度设为 N，默认 2000；是绝对值，重复运行不会叠加。");
        Console.WriteLine("  --resource C    选择资源类别；支持 IronOre/铁矿、Jujube/枣子林、HuntingAnimal/狩猎区域，也支持 all/全部。");
        Console.WriteLine("  --resource-amount N  把匹配资源点的容量和当前数量都设为 N。");
        Console.WriteLine("  --resource-lock  使用 9999999 的大储量模式；这是存档补满，不是常驻内存锁定。");
        Console.WriteLine("  图形界面可勾选“全部资源补至 99,999”，会保留每种资源自己的最大容量；采集后仍可能减少。");
        Console.WriteLine("  --resource-config N  只处理指定 ConfigID 的资源档位；不填则处理该类别全部档位。");
        Console.WriteLine("  --unit U         选择单位；支持民夫、兵种名称、自有兵种或 all/全部。");
        Console.WriteLine("                  自有兵种只筛选民夫、常规兵种和攻城器械，不包含野兽、建筑、城防设施。");
        Console.WriteLine("  --unit-instance N  只修改筛选结果中从 0 开始的一个匹配单位区域（该区域内全部字段）；不填写则修改全部匹配单位区域。");
        Console.WriteLine("  --attribute A --value N  把匹配单位的已确认整数属性全部设为 N；可用 --list-attributes 查看。");
        Console.WriteLine("  --player-attribute A --player-value N  修改 Player.sav 中匹配的玩家全局属性；搬运容量可用 AT_CartCapacity。");
        Console.WriteLine("  --save-root P   指定 SaveGames 目录；默认使用 %LOCALAPPDATA%\\MOProject\\Saved\\SaveGames。");
        Console.WriteLine("  --oodle P       指定 oo2core_9_win64.dll；默认先找 EXE 同目录，再找已验证 DLL 路径。");
        Console.WriteLine("  --yes            跳过确认提示；修改仍会在写回后完整回读校验，但不会自动备份。");
        Console.WriteLine("  --dry-run        只扫描和预览，不写文件。");
        Console.WriteLine("  --list-resources 只读列出当前 Level.sav 中所有安全识别的资源点、档位和数量。");
        Console.WriteLine("  --list-units     只读列出当前 Mass.sav 中实际发现的单位类型、数量和已知但未出现的模板。");
        Console.WriteLine("  --list-attributes 只读列出 Mass.sav 已确认可修改的单位属性和当前分布。");
        Console.WriteLine("  --list-player-attributes 只读列出 Player.sav 已识别的玩家全局属性和当前分布。");
        Console.WriteLine("  --json            让上述单位/属性/资源/玩家列表输出结构化 JSON，供图形界面读取。");
        Console.WriteLine("  --verify         只读校验 CRC、Oodle 分块、GVAS、全部当前民夫、资源点和玩家字段。");
        Console.WriteLine("  --scan-roads     只读检测道路记录；当前不会猜测写道路字段。");
        Console.WriteLine();
        Console.WriteLine("备份位置：所选槽位上两级的 Saved\\FengshaSaveEditorBackups\\。");
    }
}
