using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CloudScope.Loading;

/// <summary>The pinned LASzip C API. No platform structs or compression algorithms are duplicated in a UI/backend.</summary>
internal static unsafe class LazNative
{
    private const string Library = "cloudscope_laz";
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="cloudscope_laz_abi")] private static extern int Abi(int field);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_create")] private static extern int Create(out IntPtr handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_destroy")] private static extern int Destroy(IntPtr handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="cloudscope_laz_open_reader")] private static extern int OpenReaderCore(Handle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out int compressed, out IntPtr owner);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_close_reader")] private static extern int CloseReader(IntPtr handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_read_point")] internal static extern int ReadPoint(Handle handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_seek_point")] internal static extern int Seek(Handle handle, long index);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_get_point_pointer")] internal static extern int GetPoint(Handle handle, out IntPtr point);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_get_header_pointer")] internal static extern int GetHeader(Handle handle, out IntPtr header);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_set_header")] internal static extern int SetHeader(Handle handle, IntPtr header);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_set_point")] internal static extern int SetPoint(Handle handle, IntPtr point);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="cloudscope_laz_open_writer")] private static extern int OpenWriterCore(Handle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int compress, out IntPtr owner);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="cloudscope_laz_release_stream")] private static extern void ReleaseStream(IntPtr owner, int writer);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_write_point")] internal static extern int WritePoint(Handle handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_close_writer")] private static extern int CloseWriter(IntPtr handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl, EntryPoint="laszip_get_error")] private static extern int GetError(Handle handle, out IntPtr text);

    static LazNative()
    {
        int[] expected = [sizeof(Point), Offset(nameof(Point.GpsTime)), Offset(nameof(Point.Rgb)), Offset(nameof(Point.WavePacket)),
            Offset(nameof(Point.ExtraBytes)), Offset(nameof(Point.ExtraByteCount))];
        for (int i = 0; i < expected.Length; i++)
            if (Abi(i) != expected[i]) throw new InvalidDataException("The packaged LAZ decoder has an incompatible point layout.");
    }
    private static int Offset(string name) => (int)Marshal.OffsetOf<Point>(name);
    internal static int OpenReader(Handle handle, string path, out int compressed) =>
        OpenReaderCore(handle,path,out compressed,out handle.Stream);
    internal static int OpenWriter(Handle handle, string path, int compress) =>
        OpenWriterCore(handle,path,compress,out handle.Stream);
    internal static void Check(int result, Handle handle)
    {
        if (result == 0) return;
        GetError(handle,out IntPtr text);
        throw new InvalidDataException("LAZ decoder: " + (Marshal.PtrToStringUTF8(text) ?? "invalid compressed data"));
    }

    internal sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal bool ReaderOpen, WriterOpen;
        internal IntPtr Stream;
        internal bool WriterStream;
        internal Handle() : base(ownsHandle:true)
        {
            int result = Create(out var value); SetHandle(value);
            if (result != 0 || IsInvalid) throw new InvalidOperationException("The LAZ decoder could not be created.");
        }
        protected override bool ReleaseHandle()
        {
            if (ReaderOpen) CloseReader(handle);
            if (WriterOpen) CloseWriter(handle);
            bool result = Destroy(handle) == 0;
            if (Stream != IntPtr.Zero) ReleaseStream(Stream,WriterStream ? 1 : 0);
            return result;
        }
    }

    // laszip_api.h, pinned source revision fa089bd8. Byte fields represent the C bitfields;
    // the runtime ABI probe verifies all variable/alignment-sensitive offsets before use.
    [StructLayout(LayoutKind.Sequential, Pack=8)]
    internal struct Point
    {
        public int X,Y,Z;
        public ushort Intensity;
        public byte LegacyReturns, LegacyClassification;
        public sbyte ScanAngleRank;
        public byte UserData;
        public ushort PointSourceId;
        public short ExtendedScanAngle;
        public byte ExtendedFlags, ExtendedClassification, ExtendedReturns;
        public fixed byte Dummy[7];
        public double GpsTime;
        public fixed ushort Rgb[4];
        public fixed byte WavePacket[29];
        public int ExtraByteCount;
        public IntPtr ExtraBytes;
    }

    /// <summary>Reference-codec conversion used to prepare interoperable fixtures; no temporary LAS is used by ordinary loading.</summary>
    internal static void Transcode(string source, string destination, long pointCount, bool compressed)
    {
        using var input = new Handle(); using var output = new Handle();
        Check(OpenReader(input,source,out _),input); input.ReaderOpen = true;
        Check(GetHeader(input,out var header),input); Check(SetHeader(output,header),output);
        Check(GetPoint(input,out var point),input);
        Check(OpenWriter(output,destination,compressed ? 1 : 0),output); output.WriterOpen = true; output.WriterStream = true;
        for (long i = 0; i < pointCount; i++)
        { Check(ReadPoint(input),input); Check(SetPoint(output,point),output); Check(WritePoint(output),output); }
        Check(CloseWriter(output.DangerousGetHandle()),output); output.WriterOpen = false;
    }
}
