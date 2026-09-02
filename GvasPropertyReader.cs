using System.Buffers.Binary;
using System.Text;

namespace FengshaSaveEditor;

internal readonly record struct GvasPropertyHeader(
    int FieldOffset,
    int NameEnd,
    string Type,
    int ArrayIndex,
    int Size,
    int DataOffset);

internal readonly record struct GvasInt32Property(int FieldOffset, int ValueOffset, int Value);

/// <summary>
/// UE4 GVAS 属性读取器。UnitScanner、PlayerScanner、ResourceScanner 共用同一套解析规则，
/// 不允许任何一个 Scanner 自己算偏移。
///
/// 属性名以 FString 形式存放：[ASCII 字节][0x00]
/// 紧随其后的属性头：[int32 类型名长度][类型名 ASCII + 0x00][int32 arrayIndex][int32 size][payload...]
/// </summary>
internal static class GvasPropertyReader
{
    public const string IntPropertyType = "IntProperty";
    public const string ArrayPropertyType = "ArrayProperty";

    public static List<int> FindAll(byte[] data, byte[] pattern, int start = 0, int? end = null)
    {
        var result = new List<int>();
        var limit = end ?? data.Length;
        var position = Math.Max(0, start);
        while (position <= limit - pattern.Length)
        {
            var relative = data.AsSpan(position, limit - position).IndexOf(pattern);
            if (relative < 0)
            {
                break;
            }

            var found = position + relative;
            result.Add(found);
            position = found + 1;
        }

        return result;
    }

    public static bool IsNameCharacter(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_';
    }

    /// <summary>
    /// 属性名必须恰好等于 pattern（后面紧跟 0）。用于完整属性名匹配。
    /// </summary>
    public static int ExactNameEnd(byte[] data, int fieldOffset, byte[] pattern, int limit)
    {
        var nameEnd = checked(fieldOffset + pattern.Length);
        if (nameEnd >= limit || data[nameEnd] != 0)
        {
            return -1;
        }

        return nameEnd;
    }

    /// <summary>
    /// 属性名可能长于 pattern（例如 pattern 只是 "AT_" 前缀）。扫描到名字结尾的 0 为止。
    /// </summary>
    public static int ScanNameEnd(byte[] data, int fieldOffset, byte[] prefix, int limit)
    {
        var cursor = fieldOffset + prefix.Length;
        while (cursor < limit && IsNameCharacter(data[cursor]))
        {
            cursor++;
        }

        if (cursor >= limit || data[cursor] != 0)
        {
            return -1;
        }

        return cursor;
    }

    private static int ResolveNameEnd(byte[] data, int fieldOffset, byte[] pattern, int limit, bool prefixMatch)
    {
        return prefixMatch
            ? ScanNameEnd(data, fieldOffset, pattern, limit)
            : ExactNameEnd(data, fieldOffset, pattern, limit);
    }

    public static bool TryReadHeader(
        byte[] data,
        int fieldOffset,
        int nameEnd,
        int regionEnd,
        out GvasPropertyHeader header)
    {
        header = default;
        if (nameEnd < 0 || nameEnd >= regionEnd || data[nameEnd] != 0)
        {
            return false;
        }

        var typeLengthOffset = nameEnd + 1;
        if (typeLengthOffset + 4 > regionEnd)
        {
            return false;
        }

        var typeLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(typeLengthOffset, 4));
        if (typeLength < 1 || typeLength > 256)
        {
            return false;
        }

        var typeStart = typeLengthOffset + 4;
        var arrayIndexOffset = checked(typeStart + typeLength);
        var sizeOffset = checked(arrayIndexOffset + 4);
        var dataOffset = checked(sizeOffset + 4);
        if (typeStart + typeLength > regionEnd || sizeOffset + 4 > regionEnd || dataOffset > regionEnd)
        {
            return false;
        }

        if (data[typeStart + typeLength - 1] != 0)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(sizeOffset, 4));
        if (size < 0 || size > regionEnd - dataOffset)
        {
            return false;
        }

        header = new GvasPropertyHeader(
            fieldOffset,
            nameEnd,
            Encoding.ASCII.GetString(data, typeStart, typeLength - 1),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(arrayIndexOffset, 4)),
            size,
            dataOffset);
        return true;
    }

    /// <summary>
    /// 校验属性名之后确实是一个 size==4 的 IntProperty，并读出整数值。
    /// 只有走通这条路径的偏移才允许用于写回。
    /// </summary>
    public static bool TryReadInt32(
        byte[] data,
        int fieldOffset,
        int nameEnd,
        int regionEnd,
        out GvasInt32Property property)
    {
        property = default;
        if (!TryReadHeader(data, fieldOffset, nameEnd, regionEnd, out var header)
            || !string.Equals(header.Type, IntPropertyType, StringComparison.Ordinal)
            || header.Size != 4)
        {
            return false;
        }

        property = new GvasInt32Property(
            header.FieldOffset,
            header.DataOffset,
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(header.DataOffset, 4)));
        return true;
    }

    /// <summary>
    /// 读取本游戏在 TMap/FString 键值区域中使用的整数项：
    /// [int32 名称长度（含 0）][ASCII 名称][0][int32 值]。
    /// 只有名称前的长度与实际名称完全一致时才接受，避免把任意字符串后面的
    /// 四个字节误当成可写整数。
    /// </summary>
    public static bool TryReadMapInt32(
        byte[] data,
        int fieldOffset,
        int nameEnd,
        int regionStart,
        int regionEnd,
        out GvasInt32Property property)
    {
        property = default;
        if (nameEnd < fieldOffset
            || fieldOffset < regionStart + 4
            || nameEnd >= regionEnd
            || data[nameEnd] != 0)
        {
            return false;
        }

        var nameLength = checked(nameEnd - fieldOffset + 1);
        var lengthOffset = fieldOffset - 4;
        if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(lengthOffset, 4)) != nameLength)
        {
            return false;
        }

        var valueOffset = checked(nameEnd + 1);
        if (valueOffset > regionEnd - 4)
        {
            return false;
        }

        property = new GvasInt32Property(
            fieldOffset,
            valueOffset,
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(valueOffset, 4)));
        return true;
    }

    public static bool TryFindInt32(
        byte[] data,
        byte[] name,
        int start,
        int searchEnd,
        int regionEnd,
        out GvasInt32Property property,
        bool prefixMatch = false)
    {
        foreach (var hit in FindAll(data, name, start, searchEnd))
        {
            var nameEnd = ResolveNameEnd(data, hit, name, searchEnd, prefixMatch);
            if (TryReadInt32(data, hit, nameEnd, regionEnd, out property))
            {
                return true;
            }
        }

        property = default;
        return false;
    }

    public static List<GvasInt32Property> FindInt32Properties(
        byte[] data,
        byte[] name,
        int start,
        int searchEnd,
        int regionEnd)
    {
        var result = new List<GvasInt32Property>();
        var seen = new HashSet<int>();
        foreach (var hit in FindAll(data, name, start, searchEnd))
        {
            var nameEnd = ExactNameEnd(data, hit, name, searchEnd);
            if (TryReadInt32(data, hit, nameEnd, regionEnd, out var property)
                && seen.Add(property.ValueOffset))
            {
                result.Add(property);
            }
        }

        return result;
    }

    public static bool TryFindHeader(
        byte[] data,
        byte[] name,
        int start,
        int searchEnd,
        int regionEnd,
        string expectedType,
        out GvasPropertyHeader header)
    {
        foreach (var hit in FindAll(data, name, start, searchEnd))
        {
            var nameEnd = ExactNameEnd(data, hit, name, searchEnd);
            if (TryReadHeader(data, hit, nameEnd, regionEnd, out var candidate)
                && string.Equals(candidate.Type, expectedType, StringComparison.Ordinal))
            {
                header = candidate;
                return true;
            }
        }

        header = default;
        return false;
    }
}
