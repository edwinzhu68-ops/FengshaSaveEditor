using System.Buffers.Binary;
using System.Text;

namespace FengshaSaveEditor;

internal sealed record UnitRegionEntry(
    int RegionIndex,
    int RegionStart,
    int RegionEnd,
    string UnitType);

internal sealed record UnitAttributeEntry(
    UnitRegionEntry Region,
    string Attribute,
    int FieldOffset,
    int ValueOffset,
    int CurrentValue);

internal sealed class UnitScanResult
{
    public required int SoldierTypeRegionCount { get; init; }
    public required List<UnitRegionEntry> Regions { get; init; }
}

internal static class UnitScanner
{
    private static readonly byte[] SoldierTypeCommon = Encoding.ASCII.GetBytes("SoldierTypeCommonSaveData");
    private static readonly byte[] BaseMinFu = Encoding.ASCII.GetBytes("BaseFragmentSaveData_MinFu");
    private static readonly byte[] UnitTypeMarker = Encoding.ASCII.GetBytes("EMOSoldierType::");
    private const string PlayerOwnedUnitSelection = "@PlayerUnits";
    private static readonly HashSet<string> PlayerOwnedUnitTypeSet = new(StringComparer.OrdinalIgnoreCase)
    {
        // 民夫、常规兵种和攻城器械。野兽、城防设施、建筑和拒马不放进这个批量范围。
        "MinFu", "DunBing", "GeBing", "GongJianBing",
        "DunRuiShi", "GeRuiShi", "GongRuiShi",
        "TouShiChe", "ChuangNuChe", "ChongChe", "YunTi",
        "ZhanChe", "ZhanChe_GeBing", "ZhanChe_GongJianBing"
    };

    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minfu"] = "MinFu",
        ["民夫"] = "MinFu",
        ["gongruishi"] = "GongRuiShi",
        ["弓锐士"] = "GongRuiShi",
        ["dunruishi"] = "DunRuiShi",
        ["盾锐士"] = "DunRuiShi",
        ["geruishi"] = "GeRuiShi",
        ["戈锐士"] = "GeRuiShi",
        ["toushiche"] = "TouShiChe",
        ["投石车"] = "TouShiChe",
        ["chuangnuche"] = "ChuangNuChe",
        ["床弩车"] = "ChuangNuChe",
        ["yezhu"] = "YeZhu",
        ["野猪"] = "YeZhu",
        ["xiongpi"] = "XiongPi",
        ["熊皮"] = "XiongPi",
        ["chengfangchuangnu"] = "ChengFangChuangNu",
        ["城防床弩"] = "ChengFangChuangNu",
        ["chengfangminfang"] = "ChengFangMinFang",
        ["城防民防"] = "ChengFangMinFang",
        ["chengfangtou shiji"] = "ChengFangTouShiJi",
        ["chengfangtoushiji"] = "ChengFangTouShiJi",
        ["城防投石机"] = "ChengFangTouShiJi",
        ["chengfangwall"] = "ChengFangWall",
        ["城防墙"] = "ChengFangWall",
        ["chengfanghangtuwall"] = "ChengFangHangTuWall",
        ["夯土墙"] = "ChengFangHangTuWall",
        ["juma"] = "JuMa",
        ["拒马"] = "JuMa",
        ["自有兵种"] = PlayerOwnedUnitSelection,
        ["全部自有兵种"] = PlayerOwnedUnitSelection,
        ["playerunits"] = PlayerOwnedUnitSelection,
        ["allplayerunits"] = PlayerOwnedUnitSelection,
        ["building_large"] = "Building_Large",
        ["buildinglarge"] = "Building_Large",
        ["building_medium"] = "Building_Medium",
        ["buildingmedium"] = "Building_Medium",
        ["building_small"] = "Building_Small",
        ["buildingsmall"] = "Building_Small",
        ["all"] = "*",
        ["全部"] = "*",
        ["所有"] = "*"
    };

    private static readonly string[] KnownUnitTypeList =
    [
        "MinFu", "DunBing", "GeBing", "GongJianBing", "DunRuiShi", "GeRuiShi", "GongRuiShi",
        "TouShiChe", "ChuangNuChe", "ChongChe", "YunTi", "ZhanChe", "ZhanChe_GeBing", "ZhanChe_GongJianBing",
        "YeZhu", "XiongPi", "ChaiLang", "YeZhu_Soldier", "XiongPi_Soldier", "ChaiLang_Soldier", "PanJun",
        "ChengFangChuangNu", "ChengFangTouShiJi", "ChengFangMinFang", "ChengFangWall", "ChengFangHangTuWall", "JuMa",
        "Building_Large", "Building_Medium", "Building_Small", "Stagehand", "MenKe"
    ];

    private static readonly Dictionary<string, string> AttributeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["movespeed"] = "AT_MoveSpeed",
        ["at_movespeed"] = "AT_MoveSpeed",
        ["移速"] = "AT_MoveSpeed",
        ["maxhp"] = "AT_MaxHP",
        ["at_maxhp"] = "AT_MaxHP",
        ["最大生命"] = "AT_MaxHP",
        ["hp"] = "AT_HP",
        ["at_hp"] = "AT_HP",
        ["生命"] = "AT_HP",
        ["atk"] = "AT_Atk",
        ["at_atk"] = "AT_Atk",
        ["攻击"] = "AT_Atk",
        ["armor"] = "AT_Armor",
        ["at_armor"] = "AT_Armor",
        ["护甲"] = "AT_Armor",
        ["armorreducedamagen"] = "AT_ArmorReduceDamageN",
        ["at_armorreducedamagen"] = "AT_ArmorReduceDamageN",
        ["减伤"] = "AT_ArmorReduceDamageN",
        ["morale"] = "AT_Morale",
        ["at_morale"] = "AT_Morale",
        ["士气"] = "AT_Morale",
        ["mass"] = "AT_Mass",
        ["at_mass"] = "AT_Mass",
        ["质量"] = "AT_Mass",
        ["shield"] = "AT_Shield",
        ["at_shield"] = "AT_Shield",
        ["护盾"] = "AT_Shield",
        ["perceptionradius"] = "AT_PerceptionRadius",
        ["at_perceptionradius"] = "AT_PerceptionRadius",
        ["感知范围"] = "AT_PerceptionRadius",
        ["searchenemyrangeradius"] = "AT_SearchEnemyRangeRadius",
        ["at_searchenemyrangeradius"] = "AT_SearchEnemyRangeRadius",
        ["搜索范围"] = "AT_SearchEnemyRangeRadius",
        ["atkradiusmin"] = "AT_AtkRadiusMin",
        ["at_atkradiusmin"] = "AT_AtkRadiusMin",
        ["攻击距离最小"] = "AT_AtkRadiusMin",
        ["atkradiusmax"] = "AT_AtkRadiusMax",
        ["at_atkradiusmax"] = "AT_AtkRadiusMax",
        ["攻击距离最大"] = "AT_AtkRadiusMax",
        ["skilldamagek1"] = "AT_SkillDamageK1",
        ["at_skilldamagek1"] = "AT_SkillDamageK1",
        ["技能伤害"] = "AT_SkillDamageK1",
        ["skilllengthmodulus"] = "AT_SkillLengthModulus",
        ["at_skilllengthmodulus"] = "AT_SkillLengthModulus",
        ["技能距离倍率"] = "AT_SkillLengthModulus",
        ["proxyradius"] = "AT_ProxyRadius",
        ["at_proxyradius"] = "AT_ProxyRadius",
        ["代理半径"] = "AT_ProxyRadius",
        ["distributedpathfindingdistance"] = "AT_DistributedPathfindingDistance",
        ["at_distributedpathfindingdistance"] = "AT_DistributedPathfindingDistance",
        ["分布寻路距离"] = "AT_DistributedPathfindingDistance",
        ["buildinithp"] = "AT_BuildInitHP",
        ["at_buildinithp"] = "AT_BuildInitHP",
        ["建筑初始生命"] = "AT_BuildInitHP",
        ["maintenance"] = "AT_Maintenance",
        ["at_maintenance"] = "AT_Maintenance",
        ["维护"] = "AT_Maintenance",
        ["maintenancemax"] = "AT_MaintenanceMax",
        ["at_maintenancemax"] = "AT_MaintenanceMax",
        ["维护上限"] = "AT_MaintenanceMax",
        ["modelscale"] = "AT_ModelScale",
        ["at_modelscale"] = "AT_ModelScale",
        ["模型比例"] = "AT_ModelScale",
        ["physicsuccessprob"] = "AT_PhysicsSuccessProb",
        ["at_physicssuccessprob"] = "AT_PhysicsSuccessProb",
        ["物理成功率"] = "AT_PhysicsSuccessProb",
        ["resincreaserate"] = "AT_ResIncreaseRate",
        ["at_resincreaserate"] = "AT_ResIncreaseRate",
        ["资源增长率"] = "AT_ResIncreaseRate",
        ["fakephysicsatkswitch"] = "AT_FakePhysicsAtkSwitch",
        ["at_fakephysicsatkswitch"] = "AT_FakePhysicsAtkSwitch",
        ["物理攻击开关"] = "AT_FakePhysicsAtkSwitch",
        ["startnumrate"] = "AT_StartNumRate",
        ["at_startnumrate"] = "AT_StartNumRate",
        ["初始数量倍率"] = "AT_StartNumRate"
    };

    private static readonly Dictionary<string, string> AttributeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AT_MoveSpeed"] = "移速",
        ["AT_MaxHP"] = "最大生命",
        ["AT_HP"] = "当前生命",
        ["AT_Atk"] = "攻击",
        ["AT_Armor"] = "护甲",
        ["AT_ArmorReduceDamageN"] = "护甲减伤",
        ["AT_Morale"] = "士气",
        ["AT_Mass"] = "质量",
        ["AT_Shield"] = "护盾",
        ["AT_PerceptionRadius"] = "感知范围",
        ["AT_SearchEnemyRangeRadius"] = "搜索敌人范围",
        ["AT_AtkRadiusMin"] = "最小攻击距离",
        ["AT_AtkRadiusMax"] = "最大攻击距离",
        ["AT_SkillDamageK1"] = "技能伤害参数",
        ["AT_SkillLengthModulus"] = "技能距离倍率",
        ["AT_ProxyRadius"] = "代理半径",
        ["AT_DistributedPathfindingDistance"] = "分布寻路距离",
        ["AT_BuildInitHP"] = "建筑初始生命",
        ["AT_Maintenance"] = "维护值",
        ["AT_MaintenanceMax"] = "维护上限",
        ["AT_ModelScale"] = "模型比例",
        ["AT_PhysicsSuccessProb"] = "物理成功率参数",
        ["AT_ResIncreaseRate"] = "资源增长率参数",
        ["AT_FakePhysicsAtkSwitch"] = "物理攻击开关",
        ["AT_StartNumRate"] = "初始数量倍率"
    };

    public static IReadOnlyDictionary<string, string> SupportedAttributes => AttributeLabels;

    public static IReadOnlyList<string> KnownUnitTypes => KnownUnitTypeList;

    public static string PlayerOwnedUnitSelectionKey => PlayerOwnedUnitSelection;

    public static IReadOnlySet<string> PlayerOwnedUnitTypes => PlayerOwnedUnitTypeSet;

    public static string NormalizeUnit(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("单位类别不能为空。", nameof(value));
        }

        return UnitAliases.TryGetValue(text, out var normalized) ? normalized : text;
    }

    public static bool IsKnownSelection(string value)
    {
        var normalized = NormalizeUnit(value);
        return normalized == "*"
            || normalized.Equals(PlayerOwnedUnitSelection, StringComparison.OrdinalIgnoreCase)
            || KnownUnitTypeList.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static bool MatchesUnit(string regionType, string selectedType)
    {
        var normalized = NormalizeUnit(selectedType);
        return normalized == "*"
            || (normalized.Equals(PlayerOwnedUnitSelection, StringComparison.OrdinalIgnoreCase)
                && PlayerOwnedUnitTypeSet.Contains(regionType))
            || string.Equals(regionType, normalized, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeAttribute(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("单位属性不能为空。", nameof(value));
        }

        return AttributeAliases.TryGetValue(text, out var normalized) ? normalized : text;
    }

    public static string GetAttributeLabel(string attribute)
    {
        return AttributeLabels.TryGetValue(attribute, out var label) ? label : attribute;
    }

    public static string GetUnitLabel(string unitType)
    {
        return unitType switch
        {
            "*" => "全部单位",
            PlayerOwnedUnitSelection => "全部自有兵种",
            "MinFu" => "民夫",
            "DunBing" => "盾兵",
            "GeBing" => "戈兵",
            "GongJianBing" => "弓箭兵",
            "GongRuiShi" => "弓锐士",
            "DunRuiShi" => "盾锐士",
            "GeRuiShi" => "戈锐士",
            "TouShiChe" => "投石车",
            "ChuangNuChe" => "床弩车",
            "ChongChe" => "冲车",
            "YunTi" => "云梯",
            "ZhanChe" => "战车",
            "ZhanChe_GeBing" => "战车·戈兵",
            "ZhanChe_GongJianBing" => "战车·弓箭兵",
            "ChaiLang" => "豺狼",
            "ChaiLang_Soldier" => "豺狼兵",
            "YeZhu" => "野猪",
            "XiongPi" => "熊皮",
            "YeZhu_Soldier" => "野猪兵",
            "XiongPi_Soldier" => "熊皮兵",
            "PanJun" => "叛军",
            "ChengFangChuangNu" => "城防床弩",
            "ChengFangTouShiJi" => "城防投石机",
            "ChengFangMinFang" => "城防民防",
            "ChengFangWall" => "城防墙",
            "ChengFangHangTuWall" => "夯土墙",
            "JuMa" => "拒马",
            "Building_Large" => "大型建筑",
            "Building_Medium" => "中型建筑",
            "Building_Small" => "小型建筑",
            "Stagehand" => "场务",
            "MenKe" => "门客",
            _ => unitType
        };
    }

    public static UnitScanResult Scan(byte[] gvas)
    {
        var regionStarts = GvasPropertyReader.FindAll(gvas, SoldierTypeCommon);
        if (regionStarts.Count == 0)
        {
            throw new InvalidDataException("Mass.sav 中没有找到 SoldierTypeCommonSaveData，拒绝猜测并写入。 ");
        }

        var regions = new List<UnitRegionEntry>();
        for (var i = 0; i < regionStarts.Count; i++)
        {
            var start = regionStarts[i];
            var end = i + 1 < regionStarts.Count ? regionStarts[i + 1] : gvas.Length;
            var unitType = FindUnitType(gvas, start, end);
            if (unitType is null)
            {
                continue;
            }

            regions.Add(new UnitRegionEntry(regions.Count, start, end, unitType));
        }

        if (regions.Count == 0)
        {
            throw new InvalidDataException("Mass.sav 中没有找到可安全识别的单位类型区域，拒绝猜测并写入。 ");
        }

        return new UnitScanResult
        {
            SoldierTypeRegionCount = regionStarts.Count,
            Regions = regions
        };
    }

    public static List<UnitAttributeEntry> FindAttributeEntries(
        byte[] gvas,
        UnitScanResult scan,
        string unitType,
        string attribute,
        int? instanceIndex = null)
    {
        var result = new List<UnitAttributeEntry>();
        var normalizedUnit = NormalizeUnit(unitType);
        var normalizedAttribute = NormalizeAttribute(attribute);
        if (!AttributeLabels.ContainsKey(normalizedAttribute))
        {
            throw new ArgumentException($"未确认可以安全写回的单位属性：{attribute}。", nameof(attribute));
        }

        var matchingRegions = scan.Regions
            .Where(region => MatchesUnit(region.UnitType, normalizedUnit))
            .ToList();
        if (instanceIndex.HasValue)
        {
            if (instanceIndex.Value < 0 || instanceIndex.Value >= matchingRegions.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(instanceIndex),
                    $"单位实例编号超出范围；当前匹配到 {matchingRegions.Count} 个单位区域。 ");
            }

            matchingRegions = [matchingRegions[instanceIndex.Value]];
        }

        var pattern = Encoding.ASCII.GetBytes(normalizedAttribute);
        var unconfirmed = new List<string>();
        foreach (var region in matchingRegions)
        {
            foreach (var fieldOffset in GvasPropertyReader.FindAll(gvas, pattern, region.RegionStart, region.RegionEnd))
            {
                var nameEnd = GvasPropertyReader.ExactNameEnd(gvas, fieldOffset, pattern, region.RegionEnd);
                if (GvasPropertyReader.TryReadInt32(gvas, fieldOffset, nameEnd, region.RegionEnd, out var property)
                    || GvasPropertyReader.TryReadMapInt32(
                        gvas,
                        fieldOffset,
                        nameEnd,
                        region.RegionStart,
                        region.RegionEnd,
                        out property))
                {
                    result.Add(new UnitAttributeEntry(
                        region,
                        normalizedAttribute,
                        fieldOffset,
                        property.ValueOffset,
                        property.Value));
                }
                else if (nameEnd >= 0 && nameEnd + 1 <= region.RegionEnd - 4)
                {
                    // 两种已确认布局都没通过。记录宽松旧规则读到什么，仅用于诊断输出。
                    unconfirmed.Add(
                        $"0x{fieldOffset:X} 处未通过标准属性头/地图键值长度校验，宽松规则会读到 {BinaryPrimitives.ReadInt32LittleEndian(gvas.AsSpan(nameEnd + 1, 4))}");
                }
            }
        }

        if (result.Count == 0)
        {
            var detail = unconfirmed.Count == 0
                ? "连属性名都没有匹配到。 "
                : $"有 {unconfirmed.Count} 处属性名匹配但未通过属性头校验，前 5 处：{string.Join("；", unconfirmed.Take(5))}。 ";
            throw new InvalidDataException(
                $"在「{GetUnitLabel(normalizedUnit)}」的 {matchingRegions.Count} 个单位区域中，"
                + $"没有任何 {normalizedAttribute} 通过 GVAS 标准属性头或本游戏地图键值长度校验。"
                + detail
                + "偏移未经确认，拒绝猜测并写入。");
        }

        return result;
    }

    private static string? FindUnitType(byte[] data, int start, int end)
    {
        if (GvasPropertyReader.FindAll(data, BaseMinFu, start, end).Count > 0)
        {
            return "MinFu";
        }

        foreach (var markerOffset in GvasPropertyReader.FindAll(data, UnitTypeMarker, start, end))
        {
            var valueStart = markerOffset + UnitTypeMarker.Length;
            var valueEnd = valueStart;
            while (valueEnd < end && GvasPropertyReader.IsNameCharacter(data[valueEnd]))
            {
                valueEnd++;
            }

            if (valueEnd <= valueStart)
            {
                continue;
            }

            var unitType = Encoding.ASCII.GetString(data, valueStart, valueEnd - valueStart);
            if (!string.Equals(unitType, "None", StringComparison.OrdinalIgnoreCase))
            {
                return unitType;
            }
        }

        return null;
    }
}
