using System.Buffers.Binary;
using System.Security.Cryptography;
using CloudScope.Loading;

namespace CloudScope.Projects;

/// <summary>Identity of the loaded source payload, including original RGB and every retained attribute.</summary>
public static class ProjectSourceFingerprint
{
    public static string Create(PointCloudDataset data, CancellationToken cancellation = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[36 * 1024];
        for (int start = 0; start < data.LoadedCount; start += 1024)
        {
            cancellation.ThrowIfCancellationRequested();
            int length = Math.Min(1024, data.LoadedCount - start);
            for (int j = 0; j < length; j++)
            {
                int i = start + j; var p = data.SourcePoints[i]; var output = bytes.Slice(j * 36, 36);
                BinaryPrimitives.WriteSingleLittleEndian(output, p.X); BinaryPrimitives.WriteSingleLittleEndian(output[4..], p.Y);
                BinaryPrimitives.WriteSingleLittleEndian(output[8..], p.Z); BinaryPrimitives.WriteSingleLittleEndian(output[12..], p.R);
                BinaryPrimitives.WriteSingleLittleEndian(output[16..], p.G); BinaryPrimitives.WriteSingleLittleEndian(output[20..], p.B);
                output[24] = data.Attributes.Class[i]; BinaryPrimitives.WriteUInt16LittleEndian(output[25..], data.Attributes.Intensity[i]);
                output[27] = data.Attributes.ReturnNumber[i]; BinaryPrimitives.WriteDoubleLittleEndian(output[28..], data.Attributes.Z[i]);
            }
            hash.AppendData(bytes[..(length * 36)]);
        }
        cancellation.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
