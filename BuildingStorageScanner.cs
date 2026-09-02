using System.Buffers.Binary;
using System.Text;

namespace FengshaSaveEditor;

internal sealed record BuildingStorageItemEntry(
    string ItemType,
    int FieldOffset,
    int ValueOffset,
    int CurrentCapacity);

internal sealed record BuildingStorageRecord(
    string BuildingType,
    string BuildingLabel,
    string ActorPath,
    int StorageFieldOffset,
    int StorageDataOffset,
    int StorageDataSize,
    List<BuildingStorageItemEntry> Items);

internal sealed class BuildingStorageScanResult
{
    public required int StorageFieldCount { get; init; }
    public required int CandidateRecordCount { get; init; }
    public required int SkippedRecordCount { get; init; }
    public required List<BuildingStorageRecord> Records { get; init; }

    public int ItemCount => Records.Sum(record => record.Items.Count);
}

internal static class BuildingStorageScanner
{
    private const int ActorSearchWindow = 1_000_000;
    private const int MaxPropertyNameLength = 256;
    private const int StorageUnit = 256;

    private static readonly byte[] StorageMaxItems = Encoding.ASCII.GetBytes("StorageMaxItems");
    private static readonly byte[] StructProperty = Encoding.ASCII.GetBytes("StructProperty");
    private static readonly byte[] ItemNum = Encoding.ASCII.GetBytes("ItemNum");
    private static readonly byte[] ItemTypeMarker = Encoding.ASCII.GetBytes("EMOItemType::");
    private static readonly byte[] ActorMarker = Encoding.ASCII.GetBytes("BP_Building_");
    private static readonly byte[] GamePathMarker = Encoding.ASCII.GetBytes("/Game/");
    private static readonly byte[] FactionMarker = Encoding.ASCII.GetBytes("EMOFactionType::");

    private static readonly Dictionary<string, string> WarehouseLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BP_Building_Storehouse_C"] = "辎重库",
        ["BP_Building_Granary_C"] = "粮仓",
        ["BP_Building_Armoury_C"] = "军械库",
        ["BP_Building_Armory_C"] = "军械库"
    };

    public static IReadOnlyDictionary<string, string> KnownWarehouseLabels => WarehouseLabels;

    public static BuildingStorageScanResult Scan(byte[] gvas)
    {
        var storageFields = GvasPropertyReader.FindAll(gvas, StorageMaxItems);
        if (storageFields.Count == 0)
        {
            throw new InvalidDataException("Level.sav 中没有找到 StorageMaxItems，拒绝猜测并写入。 ");
        }

        var actors = FindBuildingActors(gvas);
        var records = new List<BuildingStorageRecord>();
        var candidateCount = 0;
        var skippedCount = 0;
        foreach (var fieldOffset in storageFields)
        {
            var nameEnd = GvasPropertyReader.ExactNameEnd(gvas, fieldOffset, StorageMaxItems, gvas.Length);
            if (!TryReadStructHeader(gvas, fieldOffset, nameEnd, gvas.Length, out var header))
            {
                skippedCount++;
                continue;
            }

            candidateCount++;
            var actor = actors
                .Where(item => item.Start < fieldOffset && fieldOffset - item.Start <= ActorSearchWindow)
                .OrderByDescending(item => item.Start)
                .FirstOrDefault();
            if (actor is null
                || !WarehouseLabels.TryGetValue(actor.BuildingType, out var buildingLabel)
                || !HasOwnedFaction(gvas, actor.Start, fieldOffset))
            {
                skippedCount++;
                continue;
            }

            var items = FindStorageItems(gvas, header.DataOffset, header.DataSize);
            if (items.Count == 0)
            {
                skippedCount++;
                continue;
            }

            records.Add(new BuildingStorageRecord(
                actor.BuildingType,
                buildingLabel,
                actor.Path,
                fieldOffset,
                header.DataOffset,
                header.DataSize,
                items));
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("Level.sav 中没有找到通过结构、归属和物品列表校验的真正仓库，拒绝猜测并写入。 ");
        }

        return new BuildingStorageScanResult
        {
            StorageFieldCount = storageFields.Count,
            CandidateRecordCount = candidateCount,
            SkippedRecordCount = skippedCount,
            Records = records
        };
    }

    public static string FormatCapacity(int rawValue)
    {
        return rawValue >= 0 && rawValue % StorageUnit == 0
            ? (rawValue / StorageUnit).ToString("N0")
            : rawValue.ToString("N0");
    }

    private static List<BuildingActor> FindBuildingActors(byte[] gvas)
    {
        var actors = new List<BuildingActor>();
        foreach (var markerOffset in GvasPropertyReader.FindAll(gvas, ActorMarker))
        {
            var pathStart = FindPathStart(gvas, markerOffset);
            if (pathStart < 0) continue;

            var pathEnd = Array.IndexOf(gvas, (byte)0, markerOffset);
            if (pathEnd <= markerOffset) continue;
            var path = Encoding.ASCII.GetString(gvas, pathStart, pathEnd - pathStart);
            var persistentLevel = path.IndexOf("PersistentLevel.", StringComparison.Ordinal);
            if (persistentLevel < 0) continue;

            var typeStart = persistentLevel + "PersistentLevel.".Length;
            var classEnd = path.IndexOf("_C_", typeStart, StringComparison.Ordinal);
            if (classEnd < 0) continue;
            var buildingType = path[typeStart..(classEnd + 2)];
            if (!WarehouseLabels.ContainsKey(buildingType)) continue;

            actors.Add(new BuildingActor(markerOffset, pathStart, path, buildingType));
        }

        return actors;
    }

    private static int FindPathStart(byte[] data, int markerOffset)
    {
        var start = Math.Max(0, markerOffset - 4096);
        var candidates = GvasPropertyReader.FindAll(data, GamePathMarker, start, markerOffset);
        return candidates.Count == 0 ? -1 : candidates[^1];
    }

    private static bool HasOwnedFaction(byte[] data, int actorStart, int storageFieldOffset)
    {
        // BuildingFactionType 位于部分建筑的主体字段中，可能早于 ObjectProperty
        // 里的 PersistentLevel 路径；从 actor 标记前留出一小段窗口，避免把同一座
        // 建筑误判成“没有归属”。如果最近的归属枚举不是赵国，则拒绝写入。
        var start = Math.Max(0, actorStart - 65_536);
        var matches = GvasPropertyReader.FindAll(data, FactionMarker, start, storageFieldOffset);
        if (matches.Count == 0) return false;

        var valueStart = matches[^1] + FactionMarker.Length;
        var valueEnd = valueStart;
        while (valueEnd < storageFieldOffset && GvasPropertyReader.IsNameCharacter(data[valueEnd])) valueEnd++;
        return valueEnd > valueStart
            && string.Equals(
                Encoding.ASCII.GetString(data, valueStart, valueEnd - valueStart),
                "ZhaoGuo",
                StringComparison.Ordinal);
    }

    private static List<BuildingStorageItemEntry> FindStorageItems(byte[] data, int dataOffset, int dataSize)
    {
        var end = checked(dataOffset + dataSize);
        var items = new List<BuildingStorageItemEntry>();
        var seen = new HashSet<int>();
        foreach (var fieldOffset in GvasPropertyReader.FindAll(data, ItemNum, dataOffset, end))
        {
            var nameEnd = GvasPropertyReader.ExactNameEnd(data, fieldOffset, ItemNum, end);
            if (!GvasPropertyReader.TryReadInt32(data, fieldOffset, nameEnd, end, out var property)
                || !seen.Add(property.ValueOffset))
            {
                continue;
            }

            items.Add(new BuildingStorageItemEntry(
                FindItemType(data, dataOffset, fieldOffset),
                property.FieldOffset,
                property.ValueOffset,
                property.Value));
        }

        return items;
    }

    private static string FindItemType(byte[] data, int start, int itemNumOffset)
    {
        var matches = GvasPropertyReader.FindAll(data, ItemTypeMarker, start, itemNumOffset);
        if (matches.Count == 0) return "未标注物品";

        var valueStart = matches[^1] + ItemTypeMarker.Length;
        var valueEnd = valueStart;
        while (valueEnd < itemNumOffset && GvasPropertyReader.IsNameCharacter(data[valueEnd])) valueEnd++;
        return valueEnd > valueStart
            ? Encoding.ASCII.GetString(data, valueStart, valueEnd - valueStart)
            : "未标注物品";
    }

    private static bool TryReadStructHeader(
        byte[] data,
        int fieldOffset,
        int nameEnd,
        int regionEnd,
        out StorageStructHeader header)
    {
        header = default;
        if (nameEnd < 0 || nameEnd >= regionEnd || data[nameEnd] != 0) return false;

        var cursor = nameEnd + 1;
        if (!TryReadAsciiString(data, ref cursor, regionEnd, out var type, out _)
            || !string.Equals(type, "StructProperty", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadInt32(data, ref cursor, regionEnd, out _)) return false;
        if (!TryReadAsciiString(data, ref cursor, regionEnd, out var structName, out _)
            || !string.Equals(structName, "MOItemNumList", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadInt32(data, ref cursor, regionEnd, out var structMarker) || structMarker != 1) return false;
        if (!TryReadAsciiString(data, ref cursor, regionEnd, out var path, out _)
            || !string.Equals(path, "/Script/MOProject", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadInt32(data, ref cursor, regionEnd, out var guidMarker) || guidMarker != 0) return false;
        if (!TryReadInt32(data, ref cursor, regionEnd, out var dataSize)
            || dataSize <= 0
            || dataSize > regionEnd - cursor)
        {
            return false;
        }

        header = new StorageStructHeader(fieldOffset, cursor, dataSize);
        return true;
    }

    private static bool TryReadAsciiString(
        byte[] data,
        ref int cursor,
        int regionEnd,
        out string value,
        out int length)
    {
        value = string.Empty;
        length = 0;
        if (!TryReadInt32(data, ref cursor, regionEnd, out length)
            || length < 1
            || length > MaxPropertyNameLength
            || cursor > regionEnd - length
            || data[cursor + length - 1] != 0)
        {
            return false;
        }

        value = Encoding.ASCII.GetString(data, cursor, length - 1);
        cursor += length;
        return true;
    }

    private static bool TryReadInt32(byte[] data, ref int cursor, int regionEnd, out int value)
    {
        value = 0;
        if (cursor > regionEnd - 4) return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(cursor, 4));
        cursor += 4;
        return true;
    }

    private sealed record BuildingActor(int Start, int PathStart, string Path, string BuildingType);

    private readonly record struct StorageStructHeader(
        int FieldOffset,
        int DataOffset,
        int DataSize);
}
