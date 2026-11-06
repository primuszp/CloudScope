using System.Buffers.Binary;
using CloudScope.Library;
using CloudScope.Library.Enums;

namespace CloudScope.Loading;

/// <summary>A streaming/seekable compressed record source for LasReader. One handle and one point buffer per file.</summary>
internal sealed unsafe class LazPointReader : IDisposable
{
    private readonly LazNative.Handle _handle;
    private readonly LasHeader _header;
    private readonly IntPtr _point;
    private readonly object _gate = new();
    private long _next;
    private bool _disposed;

    public LazPointReader(string path, LasHeader header)
    {
        _header = header; _handle = new LazNative.Handle();
        try
        {
            LazNative.Check(LazNative.OpenReader(_handle, path, out int compressed), _handle); _handle.ReaderOpen = true;
            if (compressed == 0) throw new InvalidDataException("The LAZ flag does not match the point encoding.");
            LazNative.Check(LazNative.GetPoint(_handle, out _point), _handle);
            if (_point == IntPtr.Zero) throw new InvalidDataException("The LAZ decoder did not provide a point buffer.");
        }
        catch { _handle.Dispose(); throw; }
    }

    public int Read(long start, Span<LasPoint> destination)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (start != _next) { LazNative.Check(LazNative.Seek(_handle, start), _handle); _next = start; }
            for (int i = 0; i < destination.Length; i++)
            {
                LazNative.Check(LazNative.ReadPoint(_handle), _handle);
                destination[i] = Convert(*(LazNative.Point*)_point);
                _next++;
            }
            return destination.Length;
        }
    }

    private LasPoint Convert(LazNative.Point p)
    {
        byte format = _header.PointDataFormatId;
        bool extended = format >= 6;
        byte classes = extended ? (byte)(p.ExtendedFlags >> 4) : (byte)(p.LegacyClassification >> 5);
        var result = new LasPoint
        {
            X = p.X * _header.XScaleFactor + _header.XOffset,
            Y = p.Y * _header.YScaleFactor + _header.YOffset,
            Z = p.Z * _header.ZScaleFactor + _header.ZOffset,
            Intensity = p.Intensity,
            ReturnNumber = (byte)((extended ? p.ExtendedReturns : p.LegacyReturns) & (extended ? 15 : 7)),
            NumberOfReturns = (byte)(((extended ? p.ExtendedReturns : p.LegacyReturns) >> (extended ? 4 : 3)) & (extended ? 15 : 7)),
            ScanDirectionFlag = (sbyte)((p.LegacyReturns >> 6) & 1),
            EdgeOfFlightLine = (p.LegacyReturns & 128) != 0,
            Classification = (ClassificationType)(extended ? p.ExtendedClassification : p.LegacyClassification & 31),
            ClassificationSynthetic = (classes & 1) != 0,
            ClassificationKeyPoint = (classes & 2) != 0,
            ClassificationWithheld = (classes & 4) != 0,
            ClassificationOverlap = extended && (classes & 8) != 0,
            ScannerChannel = extended ? (byte)((p.ExtendedFlags >> 2) & 3) : (byte)0,
            ScanAngleRank = extended ? (sbyte)0 : p.ScanAngleRank,
            ScanAngle = extended ? p.ExtendedScanAngle : (short)0,
            UserData = p.UserData,
            PointSourceId = p.PointSourceId,
            GpsTime = format is 0 or 2 ? 0 : p.GpsTime
        };
        if (format is 2 or 3 or 5 or 7 or 8 or 10) { result.R = p.Rgb[0]; result.G = p.Rgb[1]; result.B = p.Rgb[2]; }
        if (format is 8 or 10) result.NIR = p.Rgb[3];
        if (format is 4 or 5 or 9 or 10)
        {
            var wave = new ReadOnlySpan<byte>(p.WavePacket, 29);
            result.WavePacketDescriptorIndex = wave[0]; result.ByteOffsetToWaveformData = BinaryPrimitives.ReadUInt64LittleEndian(wave[1..]);
            result.WaveformPacketSize = BinaryPrimitives.ReadUInt32LittleEndian(wave[9..]);
            result.ReturnPointWaveformLocation = BinaryPrimitives.ReadSingleLittleEndian(wave[13..]);
            result.WaveformParamX = BinaryPrimitives.ReadSingleLittleEndian(wave[17..]); result.WaveformParamY = BinaryPrimitives.ReadSingleLittleEndian(wave[21..]);
            result.WaveformParamZ = BinaryPrimitives.ReadSingleLittleEndian(wave[25..]);
        }
        return result;
    }

    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _handle.Dispose(); } }
}
