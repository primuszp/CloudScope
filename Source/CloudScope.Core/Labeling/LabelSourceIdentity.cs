using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CloudScope.Labeling;

/// <summary>Identity of the ordered resident geometry and its coordinate frame, independent of pathname.</summary>
public sealed record LabelSourceIdentity(int PointCount, double OriginX, double OriginY, double OriginZ, string GeometrySha256)
{
    public string Algorithm => "sha256-local-xyz-v1";

    public static LabelSourceIdentity Create(ReadOnlySpan<PointData> points, int count,
        double originX = 0, double originY = 0, double originZ = 0, CancellationToken cancellation = default)
    {
        if (count < 0 || count > points.Length) throw new ArgumentOutOfRangeException(nameof(count));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[12 * 1024];
        for (int start = 0; start < count; start += 1024)
        {
            cancellation.ThrowIfCancellationRequested();
            int length = Math.Min(1024, count - start);
            for (int i = 0; i < length; i++)
            {
                PointData p = points[start + i];
                BinaryPrimitives.WriteSingleLittleEndian(bytes[(i * 12)..], p.X);
                BinaryPrimitives.WriteSingleLittleEndian(bytes[(i * 12 + 4)..], p.Y);
                BinaryPrimitives.WriteSingleLittleEndian(bytes[(i * 12 + 8)..], p.Z);
            }
            hash.AppendData(bytes[..(length * 12)]);
        }
        cancellation.ThrowIfCancellationRequested();
        return new(count, originX, originY, originZ, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
