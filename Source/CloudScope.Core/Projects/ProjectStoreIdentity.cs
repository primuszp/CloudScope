using System.Security.Cryptography;
using CloudScope.Store;

namespace CloudScope.Projects;

/// <summary>Index/layout fingerprint plus column lengths; annotated coordinates are verified separately on restore.</summary>
public static class ProjectStoreIdentity
{
    public static string Create(PointTileStore store)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var index = File.OpenRead(Path.Combine(store.Directory, PointTileStore.IndexFileName));
        var buffer = new byte[65536]; int count;
        while ((count = index.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, count));
        Span<byte> length = stackalloc byte[8];
        foreach (string file in new[] { PointTileStore.PointFileName, PointTileStore.AttributeFileName, PointTileStore.SourceIndexFileName })
        {
            string path = Path.Combine(store.Directory, file);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, File.Exists(path) ? new FileInfo(path).Length : -1);
            hash.AppendData(length);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
