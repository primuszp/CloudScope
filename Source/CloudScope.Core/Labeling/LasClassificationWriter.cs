using System.Collections.Generic;
using System.IO;
using CloudScope.Library;

namespace CloudScope.Labeling
{
    /// <summary>
    /// Writes label classification codes back into a *copy* of a LAS file. The source is never
    /// modified. Each labeled point's classification byte is overwritten in place at its record
    /// offset; for legacy formats (0-5) only the low 5 classification bits are changed so the
    /// synthetic/keypoint/withheld flags are preserved, while extended formats (6-10) take the
    /// full 0-255 byte.
    /// </summary>
    public static class LasClassificationWriter
    {
        public static string DefaultDestinationPath(string sourcePath)
        {
            string dir = Path.GetDirectoryName(sourcePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            return Path.Combine(dir, $"{name}_labeled{ext}");
        }

        /// <summary>Returns the number of point records updated.</summary>
        public static int Write(string sourcePath, string destinationPath, IReadOnlyDictionary<int, byte> sourceIndexToCode)
        {
            byte formatId;
            long pointDataOffset;
            int stride;
            long pointCount;
            using (var reader = new LasReader(sourcePath))
            {
                formatId = reader.Header.PointDataFormatId;
                pointDataOffset = reader.Header.OffsetToPointData;
                stride = reader.Header.PointDataRecordLength;
                pointCount = reader.PointCount;
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);

            bool extended = formatId >= 6;
            int classOffset = extended ? 16 : 15;

            int written = 0;
            using var fs = new FileStream(destinationPath, FileMode.Open, FileAccess.ReadWrite);
            foreach (var (index, code) in sourceIndexToCode)
            {
                if (index < 0 || index >= pointCount)
                    continue;

                long offset = pointDataOffset + (long)index * stride + classOffset;
                fs.Seek(offset, SeekOrigin.Begin);

                if (extended)
                {
                    fs.WriteByte(code);
                }
                else
                {
                    int old = fs.ReadByte();
                    if (old < 0) continue;
                    fs.Seek(offset, SeekOrigin.Begin);
                    // Preserve synthetic/keypoint/withheld flags (top 3 bits); class is 5 bits (0-31).
                    byte merged = (byte)((old & 0xE0) | (code & 0x1F));
                    fs.WriteByte(merged);
                }

                written++;
            }

            return written;
        }
    }
}
