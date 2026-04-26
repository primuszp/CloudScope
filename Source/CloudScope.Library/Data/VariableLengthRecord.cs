using System.Runtime.InteropServices;

namespace CloudScope.Library.Data
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VariableLengthRecordHeader
    {
        public ushort Reserved;
        public fixed byte UserId[16];
        public ushort RecordId;
        public ushort RecordLengthAfterHeader;
        public fixed byte Description[32];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ExtendedVariableLengthRecordHeader
    {
        public ushort Reserved;
        public fixed byte UserId[16];
        public ushort RecordId;
        public ulong RecordLengthAfterHeader;
        public fixed byte Description[32];
    }
}
