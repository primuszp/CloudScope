using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using CloudScope.Library.Data;

namespace CloudScope.Library
{
    public class VariableLengthRecordManager
    {
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly uint _numberOfVLRs;
        private readonly uint _vlrStartOffset;
        private readonly uint _pointDataOffset;

        public VariableLengthRecordManager(MemoryMappedViewAccessor accessor, uint numberOfVLRs, uint vlrStartOffset, uint pointDataOffset)
        {
            _accessor = accessor;
            _numberOfVLRs = numberOfVLRs;
            _vlrStartOffset = vlrStartOffset;
            _pointDataOffset = pointDataOffset;
        }

        public IEnumerable<(VariableLengthRecordHeader header, byte[] data)> GetVariableLengthRecords()
        {
            uint offset = _vlrStartOffset;

            for (int i = 0; i < _numberOfVLRs && offset < _pointDataOffset; i++)
            {
                var header = ReadStruct<VariableLengthRecordHeader>(offset);
                uint headerSize = 54;
                ushort dataSize = header.RecordLengthAfterHeader;

                byte[] data = new byte[dataSize];
                if (dataSize > 0)
                {
                    _accessor.ReadArray(offset + headerSize, data, 0, dataSize);
                }

                yield return (header, data);

                offset += headerSize + dataSize;
            }
        }

        public IEnumerable<(ExtendedVariableLengthRecordHeader header, byte[] data)> GetExtendedVariableLengthRecords(
            ulong evlrStartOffset, uint numberOfEVLRs, long fileSize)
        {
            ulong offset = evlrStartOffset;

            for (int i = 0; i < numberOfEVLRs && offset < (ulong)fileSize; i++)
            {
                if (offset + 60 > (ulong)fileSize) break;

                var header = ReadStruct<ExtendedVariableLengthRecordHeader>((long)offset);
                ulong headerSize = 60;
                ulong dataSize = header.RecordLengthAfterHeader;

                byte[] data = new byte[Math.Min(dataSize, (ulong)(fileSize - (long)offset - (long)headerSize))];
                if (dataSize > 0 && data.Length > 0)
                {
                    _accessor.ReadArray((long)(offset + headerSize), data, 0, data.Length);
                }

                yield return (header, data);

                offset += headerSize + dataSize;
            }
        }

        public unsafe string GetUserIdString(VariableLengthRecordHeader header)
        {
            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
                bytes[i] = header.UserId[i];
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        public unsafe string GetDescriptionString(VariableLengthRecordHeader header)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < 32; i++)
                bytes[i] = header.Description[i];
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        public unsafe string GetUserIdString(ExtendedVariableLengthRecordHeader header)
        {
            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
                bytes[i] = header.UserId[i];
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        public unsafe string GetDescriptionString(ExtendedVariableLengthRecordHeader header)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < 32; i++)
                bytes[i] = header.Description[i];
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        private T ReadStruct<T>(long offset) where T : struct
        {
            _accessor.Read(offset, out T result);
            return result;
        }
    }
}
