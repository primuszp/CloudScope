using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using CloudScope.Library.Data;
using CloudScope.Library.Enums;

namespace CloudScope.Library
{
    public sealed class LasReader : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly LasHeader _header;
        private readonly long _fileSize;
        private readonly long _pointCount;

        public LasHeader Header => _header;
        public long PointCount => _pointCount;

        public LasReader(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("LAS fĂˇjl nem talĂˇlhatĂł.", path);

            var fileInfo = new FileInfo(path);
            _fileSize = fileInfo.Length;

            _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, _fileSize, MemoryMappedFileAccess.Read);

            // Olvassuk be a headert
            _header = new LasHeader(ReadStruct<HeaderBlock>(0));

            // Alapesetben a legacy mezĹ‘bĹ‘l vesszĂĽk a pontszĂˇmot
            _pointCount = _header.NumberOfPointRecords;

            // LAS 1.4 javĂ­tĂˇs: Ha a verziĂł >= 1.4, megprĂłbĂˇljuk kiolvasni a 64 bites pontszĂˇmot
            if (_header.VersionMajor == 1 && _header.VersionMinor >= 4)
            {
                // A valĂłdi pontszĂˇm LAS 1.4-nĂ©l a 247. byte-tĂłl talĂˇlhatĂł (8 byte)
                if (_fileSize >= 247 + 8)
                {
                    ulong actualCount = _accessor.ReadUInt64(247);
                    if (actualCount > 0)
                    {
                        _pointCount = (long)actualCount;
                    }
                }
            }
        }

        private T ReadStruct<T>(long offset) where T : struct
        {
            _accessor.Read(offset, out T result);
            return result;
        }

        /// <summary>
        /// SzekvenciĂˇlisan olvassa az Ă¶sszes pontot a fĂˇjlbĂłl zero-allocation megkĂ¶zelĂ­tĂ©ssel (csak egy puffert hasznĂˇlva).
        /// A LasPoint egy struct, Ă­gy nincs szemĂ©tgyĹ±jtĂ©si overhead (GC).
        /// </summary>
        public IEnumerable<LasPoint> GetPoints()
        {
            long offset = _header.OffsetToPointData;
            int stride = _header.PointDataRecordLength;
            long total = PointCount;

            double sx = _header.XScaleFactor, ox = _header.XOffset;
            double sy = _header.YScaleFactor, oy = _header.YOffset;
            double sz = _header.ZScaleFactor, oz = _header.ZOffset;

            int colorOffset = GetColorOffset(_header.PointDataFormatId);
            
            // Chunk size
            int chunkPts = (int)Math.Min(total, 16384);
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

                    int rawX = MemoryMarshal.Read<int>(span);
                    int rawY = MemoryMarshal.Read<int>(span.Slice(4));
                    int rawZ = MemoryMarshal.Read<int>(span.Slice(8));
                    ushort intensity = MemoryMarshal.Read<ushort>(span.Slice(12));
                    
                    byte returnNumber, numberOfReturns, userData;
                    sbyte scanDir, scanAngleRank;
                    bool edgeOfFlightLine;
                    ClassificationType classification;

                    if (_header.PointDataFormatId >= 6)
                    {
                        // LAS 1.4 (Formats 6-10)
                        byte retByte = span[14];
                        returnNumber = (byte)(retByte & 0x0F);
                        numberOfReturns = (byte)((retByte >> 4) & 0x0F);
                        
                        byte flagByte = span[15];
                        scanDir = (sbyte)((flagByte >> 6) & 0x01);
                        edgeOfFlightLine = (flagByte & 0x80) != 0;
                        
                        classification = (ClassificationType)span[16];
                        userData = span[17];
                        scanAngleRank = (sbyte)MemoryMarshal.Read<short>(span.Slice(18)); // 1.4-ben 2 bĂˇjtos
                        // Point Source ID (20), GPS Time (22) - ezeket most nem tĂˇroljuk a LasPoint-ban de az offsetek stimmelnek
                    }
                    else
                    {
                        // Legacy LAS (Formats 0-5)
                        byte flagByte = span[14];
                        returnNumber = (byte)(flagByte & 0x07);
                        numberOfReturns = (byte)((flagByte >> 3) & 0x07);
                        scanDir = (sbyte)((flagByte >> 6) & 0x01);
                        edgeOfFlightLine = (flagByte & 0x80) != 0;

                        classification = (ClassificationType)(span[15] & 0x1F);
                        scanAngleRank = (sbyte)span[16];
                        userData = span[17];
                    }

                    ushort r = 0, g = 0, b = 0;
                    if (colorOffset > 0 && stride >= colorOffset + 6)
                    {
                        r = MemoryMarshal.Read<ushort>(span.Slice(colorOffset));
                        g = MemoryMarshal.Read<ushort>(span.Slice(colorOffset + 2));
                        b = MemoryMarshal.Read<ushort>(span.Slice(colorOffset + 4));
                    }

                    yield return new LasPoint
                    {
                        X = rawX * sx + ox,
                        Y = rawY * sy + oy,
                        Z = rawZ * sz + oz,
                        Intensity = intensity,
                        Classification = classification,
                        ReturnNumber = returnNumber,
                        NumberOfReturns = numberOfReturns,
                        ScanDirectionFlag = scanDir,
                        EdgeOfFlightLine = edgeOfFlightLine,
                        ScanAngleRank = scanAngleRank,
                        UserData = userData,
                        R = r,
                        G = g,
                        B = b
                    };
                }

                loaded += wantPts;
            }
        }

        private static int GetColorOffset(byte formatId)
        {
            return formatId switch
            {
                2 => 20,
                3 => 28,
                5 => 28,
                7 => 30,
                8 => 30,
                10 => 30,
                _ => 0
            };
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }
}


