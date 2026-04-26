using System;
using System.Text;
using CloudScope.Library.Data;

namespace CloudScope.Library
{
    public unsafe class LasHeader
    {
        private readonly HeaderBlock _header;

        public LasHeader(HeaderBlock header)
        {
            _header = header;
        }

        public string FileSignature
        {
            get
            {
                fixed (byte* p = _header.FileSignature)
                {
                    return new string((sbyte*)p, 0, 4, Encoding.ASCII);
                }
            }
        }

        public ushort FileSourceId => _header.FileSourceId;
        public ushort GlobalEncoding => _header.GlobalEncoding;

        public Guid ProjectId
        {
            get
            {
                byte[] guidBytes = new byte[8];
                fixed (byte* p = _header.ProjectIdGuidData4)
                {
                    for (int i = 0; i < 8; i++) guidBytes[i] = p[i];
                }
                return new Guid(
                    (int)_header.ProjectIdGuidData1, 
                    (short)_header.ProjectIdGuidData2, 
                    (short)_header.ProjectIdGuidData3, 
                    guidBytes);
            }
        }

        public byte VersionMajor => _header.VersionMajor;
        public byte VersionMinor => _header.VersionMinor;

        public string SystemIdentifier
        {
            get
            {
                fixed (byte* p = _header.SystemIdentifier)
                {
                    return new string((sbyte*)p, 0, 32, Encoding.ASCII).TrimEnd('\0');
                }
            }
        }

        public string GeneratingSoftware
        {
            get
            {
                fixed (byte* p = _header.GeneratingSoftware)
                {
                    return new string((sbyte*)p, 0, 32, Encoding.ASCII).TrimEnd('\0');
                }
            }
        }

        public ushort FileCreationDayOfYear => _header.FileCreationDayOfYear;
        public ushort FileCreationYear => _header.FileCreationYear;
        public ushort HeaderSize => _header.HeaderSize;
        public uint OffsetToPointData => _header.OffsetToPointData;
        public uint NumberOfVariableLengthRecords => _header.NumberOfVariableLengthRecords;
        public byte PointDataFormatId => _header.PointDataFormatId;
        public ushort PointDataRecordLength => _header.PointDataRecordLength;
        public uint NumberOfPointRecords => _header.NumberOfPointRecords;

        public unsafe uint[] GetNumberOfPointsByReturn()
        {
            uint[] result = new uint[5];
            fixed (uint* p = _header.NumberOfPointsByReturn)
            {
                for (int i = 0; i < 5; i++)
                    result[i] = p[i];
            }
            return result;
        }

        public double XScaleFactor => _header.XScaleFactor;
        public double YScaleFactor => _header.YScaleFactor;
        public double ZScaleFactor => _header.ZScaleFactor;

        public double XOffset => _header.XOffset;
        public double YOffset => _header.YOffset;
        public double ZOffset => _header.ZOffset;

        public double MaxX => _header.MaxX;
        public double MinX => _header.MinX;
        public double MaxY => _header.MaxY;
        public double MinY => _header.MinY;
        public double MaxZ => _header.MaxZ;
        public double MinZ => _header.MinZ;

        // LAS 1.4 Extended fields
        public ulong StartOfWaveformDataPacketRecord => _header.StartOfWaveformDataPacketRecord;
        public ulong StartOfFirstExtendedVLR => _header.StartOfFirstExtendedVLR;
        public uint NumberOfExtendedVLRs => _header.NumberOfExtendedVLRs;
        public ulong NumberOfPointRecordsExtended => _header.NumberOfPointRecordsExtended;

        public unsafe ulong[] GetNumberOfPointsByReturnExtended()
        {
            ulong[] result = new ulong[15];
            fixed (ulong* p = _header.NumberOfPointsByReturnExtended)
            {
                for (int i = 0; i < 15; i++)
                    result[i] = p[i];
            }
            return result;
        }

        public HeaderBlock GetRawBlock() => _header;
    }
}


