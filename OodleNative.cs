using System.Runtime.InteropServices;

namespace FengshaSaveEditor;

internal sealed class OodleNative : IDisposable
{
    private const int Kraken = 8;
    private const int Normal = 4;

    private IntPtr _library;
    private int _disposeState;
    private readonly OodleDecompressDelegate _decompress;
    private readonly OodleCompressDelegate _compress;
    private readonly OodleBufferSizeDelegate _getCompressedBufferSize;

    private OodleNative(
        IntPtr library,
        OodleDecompressDelegate decompress,
        OodleCompressDelegate compress,
        OodleBufferSizeDelegate getCompressedBufferSize)
    {
        _library = library;
        _decompress = decompress;
        _compress = compress;
        _getCompressedBufferSize = getCompressedBufferSize;
    }

    public string Path { get; private init; } = string.Empty;

    public static OodleNative Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 Oodle DLL。", path);
        }

        var library = NativeLibrary.Load(System.IO.Path.GetFullPath(path));
        try
        {
            var decompress = Marshal.GetDelegateForFunctionPointer<OodleDecompressDelegate>(
                NativeLibrary.GetExport(library, "OodleLZ_Decompress"));
            var compress = Marshal.GetDelegateForFunctionPointer<OodleCompressDelegate>(
                NativeLibrary.GetExport(library, "OodleLZ_Compress"));
            var getBufferSize = Marshal.GetDelegateForFunctionPointer<OodleBufferSizeDelegate>(
                NativeLibrary.GetExport(library, "OodleLZ_GetCompressedBufferSizeNeeded"));

            return new OodleNative(library, decompress, compress, getBufferSize)
            {
                Path = System.IO.Path.GetFullPath(path)
            };
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    public byte[] Decompress(byte[] compressed, int uncompressedSize)
    {
        ThrowIfDisposed();
        if (compressed.Length == 0 || uncompressedSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uncompressedSize));
        }

        var output = new byte[uncompressedSize];
        var sourceHandle = GCHandle.Alloc(compressed, GCHandleType.Pinned);
        var outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            var result = _decompress(
                sourceHandle.AddrOfPinnedObject(), compressed.Length,
                outputHandle.AddrOfPinnedObject(), output.Length,
                1, 0, 0,
                IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, 0, 3);

            if (result != uncompressedSize)
            {
                throw new InvalidDataException($"Oodle 解压长度异常：得到 {result}，应为 {uncompressedSize}。格式可能不是本游戏的存档。 ");
            }

            return output;
        }
        finally
        {
            outputHandle.Free();
            sourceHandle.Free();
        }
    }

    public byte[] Compress(byte[] raw)
    {
        ThrowIfDisposed();
        if (raw.Length == 0)
        {
            throw new ArgumentException("不能压缩空块。", nameof(raw));
        }

        long requested;
        try
        {
            requested = _getCompressedBufferSize(Kraken, raw.Length);
        }
        catch
        {
            requested = 0;
        }

        if (requested <= 0 || requested > 256L * 1024 * 1024)
        {
            requested = (long)raw.Length + raw.Length / 16 + 1024 * 1024;
        }

        if (requested > int.MaxValue)
        {
            throw new InvalidDataException("Oodle 压缩缓冲区过大。");
        }

        var output = new byte[(int)requested];
        var rawHandle = GCHandle.Alloc(raw, GCHandleType.Pinned);
        var outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            var result = _compress(
                Kraken,
                rawHandle.AddrOfPinnedObject(), raw.Length,
                outputHandle.AddrOfPinnedObject(), Normal,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, 0);

            if (result <= 0 || result > output.Length)
            {
                throw new InvalidDataException($"Oodle 压缩失败：返回 {result}，缓冲区 {output.Length}。 ");
            }

            var compressed = new byte[(int)result];
            Buffer.BlockCopy(output, 0, compressed, 0, compressed.Length);
            return compressed;
        }
        finally
        {
            outputHandle.Free();
            rawHandle.Free();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        var library = Interlocked.Exchange(ref _library, IntPtr.Zero);
        if (library != IntPtr.Zero)
        {
            NativeLibrary.Free(library);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0 || _library == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(OodleNative));
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleDecompressDelegate(
        IntPtr compressedBuffer,
        long compressedBufferSize,
        IntPtr rawBuffer,
        long rawBufferSize,
        int fuzzSafe,
        int checkCrc,
        int verbosity,
        IntPtr decBufBase,
        long decBufSize,
        IntPtr fpCallback,
        IntPtr callbackUserData,
        IntPtr decoderMemory,
        long decoderMemorySize,
        int threadPhase);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleCompressDelegate(
        int compressor,
        IntPtr rawBuffer,
        long rawLength,
        IntPtr compressedBuffer,
        int compressionLevel,
        IntPtr options,
        IntPtr dictionaryBase,
        IntPtr lrm,
        IntPtr scratchMemory,
        long scratchSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleBufferSizeDelegate(int compressor, long rawSize);
}
