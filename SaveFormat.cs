using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FengshaSaveEditor;

internal static class SaveConstants
{
    public static readonly byte[] VsomMagic = "VSOM"u8.ToArray();
    public static readonly byte[] EmsMagic = [0xC1, 0x83, 0x2A, 0x9E];
    public const int OuterHeaderSize = 0x10;
    public const int BlockHeaderSize = 0x31;
}

internal sealed record SaveBlock(
    int HeaderOffset,
    int StreamOffset,
    int CompressedSize,
    int UncompressedSize);

internal sealed class SaveContainer
{
    private readonly byte[] _fileBytes;

    private SaveContainer(byte[] fileBytes, List<SaveBlock> blocks, long uncompressedTotal)
    {
        _fileBytes = fileBytes;
        Blocks = blocks;
        UncompressedTotal = uncompressedTotal;
        StoredPayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(8, 4));
        StoredPayloadCrc = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(0x0C, 4));
        ActualPayloadCrc = Crc32.Compute(fileBytes, SaveConstants.OuterHeaderSize, fileBytes.Length - SaveConstants.OuterHeaderSize);
    }

    public IReadOnlyList<SaveBlock> Blocks { get; }
    public long UncompressedTotal { get; }
    public uint StoredPayloadSize { get; }
    public uint StoredPayloadCrc { get; }
    public uint ActualPayloadCrc { get; }
    public bool IsCrcValid => StoredPayloadCrc == ActualPayloadCrc;
    public int FileSize => _fileBytes.Length;

    public static SaveContainer Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < SaveConstants.OuterHeaderSize + SaveConstants.BlockHeaderSize)
        {
            throw new InvalidDataException($"文件太小，不是完整的 VSOM 存档：{path}");
        }

        if (!bytes.AsSpan(0, 4).SequenceEqual(SaveConstants.VsomMagic))
        {
            throw new InvalidDataException($"不是 VSOM 存档：{path}");
        }

        var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (payloadSize != bytes.Length - SaveConstants.OuterHeaderSize)
        {
            throw new InvalidDataException(
                $"VSOM 载荷长度不一致：头部 {payloadSize}，实际 {bytes.Length - SaveConstants.OuterHeaderSize}。文件可能已损坏。");
        }

        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x0C, 4));
        var actualCrc = Crc32.Compute(bytes, SaveConstants.OuterHeaderSize, bytes.Length - SaveConstants.OuterHeaderSize);
        if (storedCrc != actualCrc)
        {
            throw new InvalidDataException(
                $"VSOM CRC32 不一致：头部 0x{storedCrc:X8}，实际 0x{actualCrc:X8}。先恢复有效备份，不会继续写入。");
        }

        var blocks = new List<SaveBlock>();
        long uncompressedTotal = 0;
        var position = SaveConstants.OuterHeaderSize;
        while (position < bytes.Length)
        {
            if (bytes.Length - position < SaveConstants.BlockHeaderSize)
            {
                throw new InvalidDataException($"EMS 块头不完整，位置 0x{position:X}。");
            }

            if (!bytes.AsSpan(position, 4).SequenceEqual(SaveConstants.EmsMagic))
            {
                throw new InvalidDataException($"EMS 块魔数错误，位置 0x{position:X}。");
            }

            var compressed = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position + 0x11, 8));
            var uncompressed = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position + 0x19, 8));
            var compressedDuplicate = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position + 0x21, 8));
            var uncompressedDuplicate = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position + 0x29, 8));

            if (compressed == 0 || uncompressed == 0 || compressed != compressedDuplicate || uncompressed != uncompressedDuplicate)
            {
                throw new InvalidDataException($"EMS 块大小字段异常，位置 0x{position:X}。");
            }

            if (compressed > int.MaxValue || uncompressed > int.MaxValue)
            {
                throw new InvalidDataException("EMS 块大小超出本工具安全范围。");
            }

            var streamOffset = checked(position + SaveConstants.BlockHeaderSize);
            var blockEnd = checked(streamOffset + (int)compressed);
            if (blockEnd > bytes.Length)
            {
                throw new InvalidDataException($"EMS 压缩数据越过文件末尾，位置 0x{position:X}。");
            }

            blocks.Add(new SaveBlock(position, streamOffset, (int)compressed, (int)uncompressed));
            uncompressedTotal = checked(uncompressedTotal + (long)uncompressed);
            position = blockEnd;
        }

        if (position != bytes.Length || blocks.Count == 0)
        {
            throw new InvalidDataException("没有找到完整的 EMS 数据块。");
        }

        return new SaveContainer(bytes, blocks, uncompressedTotal);
    }

    public byte[] DecompressAll(OodleNative oodle)
    {
        if (UncompressedTotal > int.MaxValue)
        {
            throw new InvalidDataException("解压后数据超过本工具安全范围。");
        }

        var raw = new byte[(int)UncompressedTotal];
        var rawOffset = 0;
        foreach (var block in Blocks)
        {
            var compressed = new byte[block.CompressedSize];
            Buffer.BlockCopy(_fileBytes, block.StreamOffset, compressed, 0, compressed.Length);
            var uncompressed = oodle.Decompress(compressed, block.UncompressedSize);
            Buffer.BlockCopy(uncompressed, 0, raw, rawOffset, uncompressed.Length);
            rawOffset += uncompressed.Length;
        }

        return raw;
    }

    public byte[] Recompress(byte[] raw, OodleNative oodle)
    {
        if (raw.LongLength != UncompressedTotal)
        {
            throw new ArgumentException("要写回的解压数据长度与原存档不一致。", nameof(raw));
        }

        using var stream = new MemoryStream(Math.Max(FileSize, 1024));
        var outerHeader = new byte[SaveConstants.OuterHeaderSize];
        Buffer.BlockCopy(_fileBytes, 0, outerHeader, 0, outerHeader.Length);
        stream.Write(outerHeader, 0, outerHeader.Length);

        var rawOffset = 0;
        foreach (var block in Blocks)
        {
            var rawBlock = new byte[block.UncompressedSize];
            Buffer.BlockCopy(raw, rawOffset, rawBlock, 0, rawBlock.Length);
            rawOffset += rawBlock.Length;

            var compressed = oodle.Compress(rawBlock);
            var header = new byte[SaveConstants.BlockHeaderSize];
            Buffer.BlockCopy(_fileBytes, block.HeaderOffset, header, 0, header.Length);
            WriteUInt64(header, 0x11, (ulong)compressed.Length);
            WriteUInt64(header, 0x19, (ulong)rawBlock.Length);
            WriteUInt64(header, 0x21, (ulong)compressed.Length);
            WriteUInt64(header, 0x29, (ulong)rawBlock.Length);
            stream.Write(header, 0, header.Length);
            stream.Write(compressed, 0, compressed.Length);
        }

        var output = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(8, 4), checked((uint)(output.Length - SaveConstants.OuterHeaderSize)));
        var crc = Crc32.Compute(output, SaveConstants.OuterHeaderSize, output.Length - SaveConstants.OuterHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C, 4), crc);
        return output;
    }

    private static void WriteUInt64(byte[] buffer, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), value);
    }
}

internal sealed class GvasDocument
{
    private GvasDocument(byte[] raw, byte[] gvas, uint declaredLength)
    {
        Raw = raw;
        Gvas = gvas;
        DeclaredLength = declaredLength;
    }

    public byte[] Raw { get; }
    public byte[] Gvas { get; }
    public uint DeclaredLength { get; }

    public static GvasDocument Parse(byte[] raw)
    {
        if (raw.Length < 8)
        {
            throw new InvalidDataException("解压数据太短，无法读取 GVAS。");
        }

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4));
        if (declared != raw.Length - 4)
        {
            throw new InvalidDataException($"GVAS 声明长度不一致：声明 {declared}，实际 {raw.Length - 4}。");
        }

        if (!raw.AsSpan(4, 4).SequenceEqual("GVAS"u8))
        {
            throw new InvalidDataException("解压数据没有 GVAS 魔数。");
        }

        var gvas = raw.AsSpan(4, checked((int)declared)).ToArray();
        return new GvasDocument(raw, gvas, declared);
    }
}

internal static class Crc32
{
    public static uint Compute(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new ArgumentOutOfRangeException();
        }

        uint crc = 0xFFFFFFFF;
        for (var i = offset; i < offset + count; i++)
        {
            crc ^= data[i];
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}

internal static class Hashing
{
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
