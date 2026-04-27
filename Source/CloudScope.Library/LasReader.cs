using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using CloudScope.Library.Data;
using CloudScope.Library.Enums;

namespace CloudScope.Library
{
    /// <summary>
    /// LAS file reader with full support for all Point Data Record Formats (0-10),
    /// LAS versions 1.0-1.4, Variable Length Records (VLRs), and Extended VLRs.
    ///
    /// Point Data Record Format byte layout (LAS 1.4 spec):
    ///
    /// Legacy formats (0-5) — core 20 bytes:
    ///   [0-3]   X (int32)
    ///   [4-7]   Y (int32)
    ///   [8-11]  Z (int32)
    ///   [12-13] Intensity (uint16)
    ///   [14]    Return# (3b) | #Returns (3b) | ScanDir (1b) | EdgeOfFlight (1b)
    ///   [15]    Classification (5b) | Synthetic (1b) | KeyPoint (1b) | Withheld (1b)
    ///   [16]    Scan Angle Rank (int8, -90..+90)
    ///   [17]    User Data (uint8)
    ///   [18-19] Point Source ID (uint16)
    ///   Format 1, 4, 5: [20-27] GPS Time (double)
    ///   Format 2:       [20-25] RGB
    ///   Format 3, 5:    [28-33] RGB
    ///   Format 4:       [28-56] Waveform
    ///   Format 5:       [34-62] Waveform
    ///
    /// Extended formats (6-10) — core 30 bytes:
    ///   [0-3]   X (int32)
    ///   [4-7]   Y (int32)
    ///   [8-11]  Z (int32)
    ///   [12-13] Intensity (uint16)
    ///   [14]    Return# (4b) | #Returns (4b)
    ///   [15]    ClassFlags (4b) | ScannerChannel (2b) | ScanDir (1b) | EdgeOfFlight (1b)
    ///   [16]    Classification (uint8, 0-255)
    ///   [17]    User Data (uint8)
    ///   [18-19] Scan Angle (int16, 0.006 deg/unit)
    ///   [20-21] Point Source ID (uint16)
    ///   [22-29] GPS Time (double, mandatory)
    ///   Format 7, 8, 10: [30-35] RGB
    ///   Format 8:        [36-37] NIR
    ///   Format 9:        [30-58] Waveform
    ///   Format 10:       [36-37] NIR, [38-66] Waveform
    /// </summary>
    public sealed class LasReader : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly LasHeader _header;
        private readonly long _fileSize;
        private readonly long _pointCount;
        private readonly VariableLengthRecordManager _vlrManager;

        public LasHeader Header => _header;
        public long PointCount => _pointCount;
        public VariableLengthRecordManager VlrManager => _vlrManager;

        public LasReader(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("LAS file not found.", path);

            var fileInfo = new FileInfo(path);
            _fileSize = fileInfo.Length;

            _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, _fileSize, MemoryMappedFileAccess.Read);

            _header = new LasHeader(ReadStruct<HeaderBlock>(0));

            // Legacy 32-bit point count
            _pointCount = _header.NumberOfPointRecords;

            // LAS 1.4: Use 64-bit point count from offset 247 if available
            if (_header.VersionMajor == 1 && _header.VersionMinor >= 4)
            {
                if (_fileSize >= 247 + 8)
                {
                    ulong actualCount = _accessor.ReadUInt64(247);
                    if (actualCount > 0)
                        _pointCount = (long)actualCount;
                }
            }

            _vlrManager = new VariableLengthRecordManager(
                _accessor,
                _header.NumberOfVariableLengthRecords,
                (uint)(_header.HeaderSize),
                _header.OffsetToPointData
            );
        }

        private T ReadStruct<T>(long offset) where T : struct
        {
            _accessor.Read(offset, out T result);
            return result;
        }

        /// <summary>
        /// Streams all point records using a single reused buffer (zero GC allocation per point).
        /// Supports all Point Data Record Formats 0-10.
        /// For maximum throughput on large files use FillBuffer() instead.
        /// </summary>
        public IEnumerable<LasPoint> GetPoints()
        {
            byte formatId = _header.PointDataFormatId;
            long offset = _header.OffsetToPointData;
            int stride = _header.PointDataRecordLength;
            long total = PointCount;

            double sx = _header.XScaleFactor, ox = _header.XOffset;
            double sy = _header.YScaleFactor, oy = _header.YOffset;
            double sz = _header.ZScaleFactor, oz = _header.ZOffset;

            int chunkPts = (int)Math.Min(total, 65536);
            byte[] buffer = new byte[chunkPts * stride];
            long loaded = 0;

            while (loaded < total)
            {
                long remaining = total - loaded;
                int wantPts = (int)Math.Min(chunkPts, remaining);
                int wantBytes = wantPts * stride;

                _accessor.ReadArray(offset, buffer, 0, wantBytes);
                offset += wantBytes;

                for (int i = 0; i < wantPts; i++)
                {
                    ReadOnlySpan<byte> span = buffer.AsSpan(i * stride, stride);
                    var point = ParsePoint(span, formatId, sx, ox, sy, oy, sz, oz);
                    yield return point;
                }

                loaded += wantPts;
            }
        }

        /// <summary>
        /// Fills a pre-allocated array with parsed points. Uses sequential I/O (one chunk at
        /// a time) so the OS read-ahead prefetcher stays effective, then parses each chunk in
        /// parallel across all CPU cores.  Prefer GetPoints() for streaming use-cases to avoid
        /// allocating a large intermediate LasPoint[].
        /// Returns the number of points written.
        /// </summary>
        public long FillBuffer(LasPoint[] buffer, long maxPoints = 0)
        {
            long count = maxPoints > 0
                ? Math.Min(maxPoints, Math.Min(PointCount, (long)buffer.Length))
                : Math.Min(PointCount, (long)buffer.Length);

            byte formatId = _header.PointDataFormatId;
            long dataOffset = _header.OffsetToPointData;
            int stride = _header.PointDataRecordLength;
            double sx = _header.XScaleFactor, ox = _header.XOffset;
            double sy = _header.YScaleFactor, oy = _header.YOffset;
            double sz = _header.ZScaleFactor, oz = _header.ZOffset;

            // 128 K points per chunk — large enough for parallel parse to amortise overhead,
            // small enough to stay in L3 cache and keep sequential I/O read-ahead happy.
            const int ChunkPts = 1 << 17;
            int rawSize = (int)Math.Min(ChunkPts, count) * stride;
            byte[] raw = new byte[rawSize];
            long processed = 0;

            while (processed < count)
            {
                int batch = (int)Math.Min(count - processed, ChunkPts);
                int batchBytes = batch * stride;

                // Sequential read — lets the OS prefetch the next chunk while we parse this one.
                _accessor.ReadArray(dataOffset + processed * stride, raw, 0, batchBytes);

                // Parallel parse — pure CPU work, no I/O.
                long procSnapshot = processed;
                System.Threading.Tasks.Parallel.For(0, batch, i =>
                {
                    buffer[procSnapshot + i] = ParsePoint(
                        raw.AsSpan(i * stride, stride),
                        formatId, sx, ox, sy, oy, sz, oz);
                });

                processed += batch;
            }

            return count;
        }

        private static LasPoint ParsePoint(
            ReadOnlySpan<byte> data, byte formatId,
            double sx, double ox, double sy, double oy, double sz, double oz)
        {
            var p = new LasPoint();

            // Core XYZ + Intensity: present in all formats
            p.X = MemoryMarshal.Read<int>(data) * sx + ox;
            p.Y = MemoryMarshal.Read<int>(data.Slice(4)) * sy + oy;
            p.Z = MemoryMarshal.Read<int>(data.Slice(8)) * sz + oz;
            p.Intensity = MemoryMarshal.Read<ushort>(data.Slice(12));

            if (formatId <= 5)
                ParseLegacy(data, formatId, ref p);
            else
                ParseExtended(data, formatId, ref p);

            return p;
        }

        // ── Legacy formats (0-5) ────────────────────────────────────────────────
        private static void ParseLegacy(ReadOnlySpan<byte> data, byte fmt, ref LasPoint p)
        {
            // Byte 14: return info (3+3+1+1 bits)
            byte retByte = data[14];
            p.ReturnNumber    = (byte)(retByte & 0x07);
            p.NumberOfReturns = (byte)((retByte >> 3) & 0x07);
            p.ScanDirectionFlag = (sbyte)((retByte >> 6) & 0x01);
            p.EdgeOfFlightLine  = (retByte & 0x80) != 0;

            // Byte 15: classification byte (5b class + 3b flags)
            byte cls = data[15];
            p.Classification          = (ClassificationType)(cls & 0x1F);
            p.ClassificationSynthetic = (cls & 0x20) != 0;
            p.ClassificationKeyPoint  = (cls & 0x40) != 0;
            p.ClassificationWithheld  = (cls & 0x80) != 0;

            p.ScanAngleRank = (sbyte)data[16];
            p.UserData      = data[17];
            p.PointSourceId = MemoryMarshal.Read<ushort>(data.Slice(18));

            // Format 1, 3, 4, 5: GPS Time at byte 20
            if (fmt == 1 || fmt == 3 || fmt == 4 || fmt == 5)
                p.GpsTime = MemoryMarshal.Read<double>(data.Slice(20));

            // Format 2: RGB at byte 20
            if (fmt == 2)
            {
                p.R = MemoryMarshal.Read<ushort>(data.Slice(20));
                p.G = MemoryMarshal.Read<ushort>(data.Slice(22));
                p.B = MemoryMarshal.Read<ushort>(data.Slice(24));
            }

            // Format 3, 5: RGB at byte 28 (after GPS Time)
            if (fmt == 3 || fmt == 5)
            {
                p.R = MemoryMarshal.Read<ushort>(data.Slice(28));
                p.G = MemoryMarshal.Read<ushort>(data.Slice(30));
                p.B = MemoryMarshal.Read<ushort>(data.Slice(32));
            }

            // Format 4: Waveform at byte 28 (after GPS Time, no RGB)
            if (fmt == 4)
                ParseWaveform(data, 28, ref p);

            // Format 5: Waveform at byte 34 (after GPS Time + RGB)
            if (fmt == 5)
                ParseWaveform(data, 34, ref p);
        }

        // ── Extended formats (6-10) ─────────────────────────────────────────────
        private static void ParseExtended(ReadOnlySpan<byte> data, byte fmt, ref LasPoint p)
        {
            // Byte 14: return number (4b) + number of returns (4b)
            byte retByte = data[14];
            p.ReturnNumber    = (byte)(retByte & 0x0F);
            p.NumberOfReturns = (byte)((retByte >> 4) & 0x0F);

            // Byte 15: classification flags (4b) + scanner channel (2b) + scan dir (1b) + edge (1b)
            byte flags = data[15];
            p.ClassificationSynthetic = (flags & 0x01) != 0;
            p.ClassificationKeyPoint  = (flags & 0x02) != 0;
            p.ClassificationWithheld  = (flags & 0x04) != 0;
            p.ClassificationOverlap   = (flags & 0x08) != 0;
            p.ScannerChannel          = (byte)((flags >> 4) & 0x03);
            p.ScanDirectionFlag       = (sbyte)((flags >> 6) & 0x01);
            p.EdgeOfFlightLine        = (flags & 0x80) != 0;

            // Byte 16: classification (full byte = 0-255)
            p.Classification = (ClassificationType)data[16];
            p.UserData       = data[17];

            // Byte 18-19: scan angle (int16, 0.006 deg/unit, -30000..+30000)
            p.ScanAngle     = MemoryMarshal.Read<short>(data.Slice(18));
            p.PointSourceId = MemoryMarshal.Read<ushort>(data.Slice(20));

            // Byte 22-29: GPS Time (mandatory for all extended formats)
            p.GpsTime = MemoryMarshal.Read<double>(data.Slice(22));

            // Format 7, 8, 10: RGB at byte 30
            if (fmt == 7 || fmt == 8 || fmt == 10)
            {
                p.R = MemoryMarshal.Read<ushort>(data.Slice(30));
                p.G = MemoryMarshal.Read<ushort>(data.Slice(32));
                p.B = MemoryMarshal.Read<ushort>(data.Slice(34));
            }

            // Format 8: NIR at byte 36  (after RGB)
            if (fmt == 8)
                p.NIR = MemoryMarshal.Read<ushort>(data.Slice(36));

            // Format 10: NIR at byte 36  (after RGB, before Waveform)
            if (fmt == 10)
                p.NIR = MemoryMarshal.Read<ushort>(data.Slice(36));

            // Format 9:  Waveform at byte 30 (no RGB/NIR before it)
            if (fmt == 9)
                ParseWaveform(data, 30, ref p);

            // Format 10: Waveform at byte 38 (after RGB 6B + NIR 2B)
            if (fmt == 10)
                ParseWaveform(data, 38, ref p);
        }

        // ── Waveform packet fields ──────────────────────────────────────────────
        // Waveform Packet Descriptor (29 bytes total):
        //   [0]    Wave Packet Descriptor Index (uint8)
        //   [1-8]  Byte offset to waveform data (uint64)
        //   [9-12] Waveform packet size in bytes (uint32)
        //   [13-16] Return Point Waveform Location (float)
        //   [17-20] X(t) (float)
        //   [21-24] Y(t) (float)
        //   [25-28] Z(t) (float)
        private static void ParseWaveform(ReadOnlySpan<byte> data, int offset, ref LasPoint p)
        {
            p.WavePacketDescriptorIndex  = data[offset];
            p.ByteOffsetToWaveformData   = MemoryMarshal.Read<ulong>(data.Slice(offset + 1));
            p.WaveformPacketSize         = MemoryMarshal.Read<uint>(data.Slice(offset + 9));
            p.ReturnPointWaveformLocation = MemoryMarshal.Read<float>(data.Slice(offset + 13));
            p.WaveformParamX             = MemoryMarshal.Read<float>(data.Slice(offset + 17));
            p.WaveformParamY             = MemoryMarshal.Read<float>(data.Slice(offset + 21));
            p.WaveformParamZ             = MemoryMarshal.Read<float>(data.Slice(offset + 25));
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }
}
