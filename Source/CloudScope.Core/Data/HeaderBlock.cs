using System.Runtime.InteropServices;

namespace CloudScope.Library.Data
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct HeaderBlock
    {
        public fixed byte FileSignature[4]; // "LASF"

        public ushort FileSourceId;
        public ushort GlobalEncoding;
        
        public uint ProjectIdGuidData1;
        public ushort ProjectIdGuidData2;
        public ushort ProjectIdGuidData3;
        
        public fixed byte ProjectIdGuidData4[8];

        public byte VersionMajor;
        public byte VersionMinor;

        public fixed byte SystemIdentifier[32];

        public fixed byte GeneratingSoftware[32];

        public ushort FileCreationDayOfYear;
        public ushort FileCreationYear;
        public ushort HeaderSize;
        public uint OffsetToPointData;
        public uint NumberOfVariableLengthRecords;
        public byte PointDataFormatId;
        public ushort PointDataRecordLength;
        public uint NumberOfPointRecords;

        public fixed uint NumberOfPointsByReturn[5];

        public double XScaleFactor;
        public double YScaleFactor;
        public double ZScaleFactor;
        public double XOffset;
        public double YOffset;
        public double ZOffset;
        public double MaxX;
        public double MinX;
        public double MaxY;
        public double MinY;
        public double MaxZ;
        public double MinZ;

        // LAS 1.4 Extensions (offset 227)
        public ulong StartOfWaveformDataPacketRecord;
        public ulong StartOfFirstExtendedVLR;
        public uint NumberOfExtendedVLRs;
        public ulong NumberOfPointRecordsExtended;

        // LAS 1.4 Extended number of points by return (15 returns)
        public fixed ulong NumberOfPointsByReturnExtended[15];
    }
}

