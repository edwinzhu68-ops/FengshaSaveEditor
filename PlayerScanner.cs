using System.Buffers.Binary;
using System.Text;

namespace FengshaSaveEditor;

internal sealed record PlayerAttributeEntry(
    string Attribute,
    int FieldOffset,
    int ValueOffset,
    int CurrentValue);

internal sealed class PlayerScanResult
{
    public required List<PlayerAttributeEntry> Entries { get; init; }

    public IReadOnlyList<string> AttributeNames => Entries
        .Select(entry => entry.Attribute)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

internal static class PlayerScanner
{
    private static readonly byte[] AttributePrefix = Encoding.ASCII.GetBytes("AT_");
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cartcapacity"] = "AT_CartCapacity",
        ["搬运数量"] = "AT_CartCapacity",
        ["搬运容量"] = "AT_CartCapacity",
        ["carrycapacity"] = "AT_CartCapacity",
        ["carryefficiency"] = "AT_CarryEfficiency",
        ["搬运效率"] = "AT_CarryEfficiency",
        ["collectefficiency"] = "AT_CollectEfficiency",
        ["采集效率"] = "AT_CollectEfficiency",
        ["craftefficiency"] = "AT_CraftEfficiency",
        ["制作效率"] = "AT_CraftEfficiency",
        ["policymovespeed"] = "AT_PolicyMoveSpeed",
        ["政策移速"] = "AT_PolicyMoveSpeed",
        ["roadtoll"] = "AT_RoadToll",
        ["道路税"] = "AT_RoadToll"
    };

    private static readonly Dictionary<string, string> AttributeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AT_ArmyCreateNumRate"] = "军队创建数量倍率",
        ["AT_CarryEfficiency"] = "搬运效率",
        ["AT_CartCapacity"] = "搬运数量",
        ["AT_CollectEfficiency"] = "采集效率",
        ["AT_CraftEfficiency"] = "制作效率",
        ["AT_DamageToEnemyBuildingRate"] = "对建筑伤害倍率",
        ["AT_DeadTaxation"] = "死亡税",
        ["AT_DefaultRangedSkillLengthRate"] = "远程技能距离倍率",
        ["AT_DiplomacyUnLockCondition"] = "外交解锁条件",
        ["AT_DisasterTarget"] = "灾害目标",
        ["AT_EnemyBecomeAllyProb"] = "敌军转友军概率",
        ["AT_FakePhysicsAtkSwitch"] = "物理攻击开关",
        ["AT_FakePhysicsBuffSwitch"] = "物理增益开关",
        ["AT_FarmlandOutput"] = "农田产出",
        ["AT_FoodPenaltyReduction"] = "食物惩罚减免",
        ["AT_GloablExtraResourceSwitch"] = "全局额外资源开关",
        ["AT_GlobalFakePhysicsSwitch"] = "全局物理开关",
        ["AT_GlobalFertilityCostAttenuationCoef"] = "全局肥力消耗衰减",
        ["AT_HouCrewDecrease"] = "侯级劳力减少",
        ["AT_HouCrewIncrease"] = "侯级劳力增加",
        ["AT_HPAddPerTime"] = "周期生命恢复",
        ["AT_IgnoreRangedDamageProb"] = "远程伤害免疫概率",
        ["AT_Illness"] = "疾病",
        ["AT_Injury"] = "受伤",
        ["AT_JunCrewDecrease"] = "郡级劳力减少",
        ["AT_JunCrewIncrease"] = "郡级劳力增加",
        ["AT_MaxDisasterEventAdjunctNum"] = "最大灾害事件附加数",
        ["AT_MaxTransEnemyNum"] = "最大转化敌人数",
        ["AT_MenKeGlobalEffect_CostExemption"] = "门客效果：费用豁免",
        ["AT_MenKeGlobalEffect_IgnoreTotalLimit"] = "门客效果：忽略总上限",
        ["AT_MenKeGlobalEffect_RefreshAllSchool"] = "门客效果：刷新全部学派",
        ["AT_MinDisasterEventAdjunctNum"] = "最小灾害事件附加数",
        ["AT_PhysicsSuccessProb"] = "物理成功率",
        ["AT_PolicyMoveSpeed"] = "政策移速",
        ["AT_RebelRate"] = "叛乱率",
        ["AT_RebelThunderKillProb"] = "雷击叛军击杀概率",
        ["AT_RitualRefundRatio"] = "仪式退款比例",
        ["AT_RoadToll"] = "道路税",
        ["AT_SODaoBiNum_Altar"] = "祭坛掉落钱币数量",
        ["AT_SODaoBiNum_CivilianTent"] = "民居掉落钱币数量",
        ["AT_SODaoBiNum_Hospital"] = "医院掉落钱币数量",
        ["AT_SODaoBiNum_Kitchen"] = "厨房掉落钱币数量",
        ["AT_SODaoBiNum_Planting"] = "种植掉落钱币数量",
        ["AT_SODaoBiProbability_Altar"] = "祭坛掉落概率",
        ["AT_SODaoBiProbability_CivilianTent"] = "民居掉落概率",
        ["AT_SODaoBiProbability_Hospital"] = "医院掉落概率",
        ["AT_SODaoBiProbability_Kitchen"] = "厨房掉落概率",
        ["AT_SODaoBiProbability_Planting"] = "种植掉落概率",
        ["AT_SoldierExplodeDmgUp"] = "士兵爆炸伤害提升",
        ["AT_SOMenKeExtraOutputProbability"] = "门客额外产出概率",
        ["AT_SO_OutputDaoBiNum_CivilianTent"] = "民居额外钱币数量",
        ["AT_SO_OutputDaoBiProbability_CivilianTent"] = "民居额外钱币概率",
        ["AT_TimeInterval"] = "时间间隔",
        ["AT_TradeDiscount"] = "交易折扣",
        ["AT_WallBuildEfficiency"] = "城墙建造效率",
        ["AT_WangCrewDecrease"] = "王级劳力减少",
        ["AT_WangCrewIncrease"] = "王级劳力增加"
    };

    public static string NormalizeAttribute(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("玩家属性不能为空。", nameof(value));
        }

        if (Aliases.TryGetValue(text, out var alias))
        {
            return alias;
        }

        return text.StartsWith("AT_", StringComparison.OrdinalIgnoreCase)
            ? text
            : "AT_" + text;
    }

    public static PlayerScanResult Scan(byte[] gvas)
    {
        var entries = new List<PlayerAttributeEntry>();
        var unconfirmed = new List<string>();
        foreach (var fieldOffset in GvasPropertyReader.FindAll(gvas, AttributePrefix))
        {
            var nameEnd = GvasPropertyReader.ScanNameEnd(gvas, fieldOffset, AttributePrefix, gvas.Length);
            if (nameEnd < 0)
            {
                continue;
            }

            var name = Encoding.ASCII.GetString(gvas, fieldOffset, nameEnd - fieldOffset);
            if (GvasPropertyReader.TryReadInt32(gvas, fieldOffset, nameEnd, gvas.Length, out var property)
                || GvasPropertyReader.TryReadMapInt32(
                    gvas,
                    fieldOffset,
                    nameEnd,
                    0,
                    gvas.Length,
                    out property))
            {
                entries.Add(new PlayerAttributeEntry(name, fieldOffset, property.ValueOffset, property.Value));
            }
            else if (nameEnd + 1 <= gvas.Length - 4)
            {
                // 两种已确认布局都没通过。记录宽松旧规则读到什么，仅用于诊断输出。
                unconfirmed.Add(
                    $"{name} @0x{fieldOffset:X} 未通过标准属性头/地图键值长度校验，宽松规则会读到 {BinaryPrimitives.ReadInt32LittleEndian(gvas.AsSpan(nameEnd + 1, 4))}");
            }
        }

        if (entries.Count == 0)
        {
            var detail = unconfirmed.Count == 0
                ? "连 AT_ 属性名都没有匹配到。"
                : $"有 {unconfirmed.Count} 处 AT_ 名称匹配但未通过属性头校验，前 8 处：{string.Join("；", unconfirmed.Take(8))}。";
            throw new InvalidDataException(
                "Player.sav 中没有找到通过 GVAS 标准属性头或本游戏地图键值长度校验的 AT_ 字段。"
                + detail
                + "如果这些字段是以 TMap 之类非标准属性布局存放的，偏移规则需要单独确认；"
                + "本工具不会用未确认的偏移写入。");
        }

        return new PlayerScanResult { Entries = entries };
    }

    public static List<PlayerAttributeEntry> FindAttributeEntries(
        PlayerScanResult scan,
        string attribute)
    {
        var normalized = NormalizeAttribute(attribute);
        return scan.Entries
            .Where(entry => string.Equals(entry.Attribute, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string GetLabel(string attribute)
    {
        return AttributeLabels.TryGetValue(attribute, out var label) ? label : "其他已识别属性";
    }
}
