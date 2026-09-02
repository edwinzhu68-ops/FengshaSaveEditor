using System.Text;

namespace FengshaSaveEditor;

internal sealed record ResourceNodeEntry(
    int RecordIndex,
    int RegionStart,
    int RegionEnd,
    string Category,
    int ConfigId,
    int ResourceSaveId,
    int CapacityFieldOffset,
    int CapacityValueOffset,
    int CurrentCapacity,
    int ItemFieldOffset,
    int ItemValueOffset,
    int CurrentAmount);

internal sealed class ResourceScanResult
{
    public required int ResourceSaveIdFieldCount { get; init; }
    public required int CandidateRecordCount { get; init; }
    public required int SkippedRecordCount { get; init; }
    public required List<ResourceNodeEntry> Nodes { get; init; }
}

internal static class ResourceScanner
{
    private const int MetadataScanLimit = 0x8000;
    private static readonly byte[] ResourceSaveId = Encoding.ASCII.GetBytes("ResourceSaveID");
    private static readonly byte[] ConfigId = Encoding.ASCII.GetBytes("ConfigID");
    private static readonly byte[] Capacity = Encoding.ASCII.GetBytes("Capacity");
    private static readonly byte[] Items = Encoding.ASCII.GetBytes("Items");
    private static readonly byte[] ItemNum = Encoding.ASCII.GetBytes("ItemNum");
    private static readonly byte[] ItemTypeMarker = Encoding.ASCII.GetBytes("EMOItemType::");
    private static readonly string[] LockListFields =
    [
        "GetItemLockToken2ItemList",
        "GetItemTotalLockedItemNumList",
        "PutItemLockToken2ItemList",
        "PutItemTotalLockedItemNumList"
    ];

    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HuntingAnimal"] = "狩猎区域",
        ["BeastMaterial"] = "兽材",
        ["IronOre"] = "铁矿",
        ["SilverOre"] = "银矿",
        ["CopperOre"] = "铜矿",
        ["TinOre"] = "锡矿",
        ["StoneRaw"] = "石料",
        ["RawJade"] = "原玉",
        ["Salt"] = "盐",
        ["Herb"] = "草药",
        ["Jujube"] = "枣子林",
        ["Clay"] = "黏土",
        ["Log"] = "木材/树林",
        ["Charcoal"] = "木炭"
    };

    public static IReadOnlyDictionary<string, string> KnownCategoryLabels => CategoryLabels;

    public static string NormalizeCategory(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("资源类别不能为空。", nameof(value));
        }

        return text.ToLowerInvariant() switch
        {
            "all" or "全部" or "所有" => "*",
            "hunting" or "hunt" or "狩猎" or "狩猎区域" or "huntinganimal" => "HuntingAnimal",
            "iron" or "铁" or "铁矿" or "ironore" => "IronOre",
            "silver" or "银" or "银矿" or "silverore" => "SilverOre",
            "copper" or "铜" or "铜矿" or "copperore" => "CopperOre",
            "tin" or "锡" or "锡矿" or "tinore" => "TinOre",
            "stone" or "石" or "石料" or "stone raw" or "stoneraw" => "StoneRaw",
            "jade" or "玉" or "原玉" or "rawjade" => "RawJade",
            "salt" or "盐" => "Salt",
            "herb" or "草药" => "Herb",
            "jujube" or "枣子" or "枣子林" => "Jujube",
            "clay" or "黏土" or "粘土" => "Clay",
            "log" or "木材" or "树林" => "Log",
            "charcoal" or "木炭" => "Charcoal",
            _ => text
        };
    }

    public static string GetCategoryLabel(string category)
    {
        if (category == "*") return "全部资源";
        return CategoryLabels.TryGetValue(category, out var label) ? label : category;
    }

    public static string GetSizeLabel(IEnumerable<int> capacities, int capacity)
    {
        var ordered = capacities
            .Where(value => value > 0)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        if (ordered.Count <= 1) return "单一规模";

        var index = ordered.IndexOf(capacity);
        if (index < 0) return "未分档";
        if (index == 0) return "小型";
        if (index == ordered.Count - 1) return "大型";
        return "中型";
    }

    public static ResourceScanResult Scan(byte[] gvas)
    {
        var recordStarts = GvasPropertyReader.FindAll(gvas, ResourceSaveId);
        if (recordStarts.Count == 0)
        {
            throw new InvalidDataException("Level.sav 中没有找到 ResourceSaveID，拒绝猜测并写入。 ");
        }

        var nodes = new List<ResourceNodeEntry>();
        var candidateCount = 0;
        var skippedCount = 0;
        for (var i = 0; i < recordStarts.Count; i++)
        {
            var start = recordStarts[i];
            var end = i + 1 < recordStarts.Count ? recordStarts[i + 1] : gvas.Length;
            var idNameEnd = GvasPropertyReader.ExactNameEnd(gvas, start, ResourceSaveId, end);
            if (!GvasPropertyReader.TryReadInt32(gvas, start, idNameEnd, end, out var resourceId))
            {
                continue;
            }

            candidateCount++;
            var metadataEnd = Math.Min(end, checked(start + MetadataScanLimit));
            if (!GvasPropertyReader.TryFindInt32(gvas, ConfigId, start, metadataEnd, end, out var config)
                || !GvasPropertyReader.TryFindInt32(gvas, Capacity, start, metadataEnd, end, out var capacity)
                || !GvasPropertyReader.TryFindHeader(gvas, Items, start, metadataEnd, end, GvasPropertyReader.ArrayPropertyType, out var itemsHeader))
            {
                skippedCount++;
                continue;
            }

            var category = FindCategory(gvas, start, end);
            if (string.IsNullOrEmpty(category))
            {
                skippedCount++;
                continue;
            }

            var itemsEnd = GetItemsEnd(gvas, itemsHeader.FieldOffset, end);
            var itemEntries = GvasPropertyReader.FindInt32Properties(gvas, ItemNum, itemsHeader.FieldOffset + 1, itemsEnd, end);
            if (itemEntries.Count != 1)
            {
                skippedCount++;
                continue;
            }

            var item = itemEntries[0];
            nodes.Add(new ResourceNodeEntry(
                nodes.Count,
                start,
                end,
                category,
                config.Value,
                resourceId.Value,
                capacity.FieldOffset,
                capacity.ValueOffset,
                capacity.Value,
                item.FieldOffset,
                item.ValueOffset,
                item.Value));
        }

        if (nodes.Count == 0)
        {
            throw new InvalidDataException("Level.sav 中没有找到可安全识别的资源点，拒绝猜测并写入。 ");
        }

        return new ResourceScanResult
        {
            ResourceSaveIdFieldCount = recordStarts.Count,
            CandidateRecordCount = candidateCount,
            SkippedRecordCount = skippedCount,
            Nodes = nodes
        };
    }

    private static int GetItemsEnd(byte[] gvas, int itemsFieldOffset, int regionEnd)
    {
        var fallback = regionEnd;
        foreach (var field in LockListFields)
        {
            var marker = Encoding.ASCII.GetBytes(field);
            var hit = GvasPropertyReader.FindAll(gvas, marker, itemsFieldOffset + 1, regionEnd).FirstOrDefault(-1);
            if (hit >= 0)
            {
                fallback = Math.Min(fallback, hit);
            }
        }

        return fallback;
    }

    private static string? FindCategory(byte[] data, int start, int end)
    {
        foreach (var markerOffset in GvasPropertyReader.FindAll(data, ItemTypeMarker, start, end))
        {
            var valueStart = markerOffset + ItemTypeMarker.Length;
            var valueEnd = valueStart;
            while (valueEnd < end && GvasPropertyReader.IsNameCharacter(data[valueEnd]))
            {
                valueEnd++;
            }

            if (valueEnd <= valueStart)
            {
                continue;
            }

            var category = Encoding.ASCII.GetString(data, valueStart, valueEnd - valueStart);
            if (!string.Equals(category, "None", StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }
}
