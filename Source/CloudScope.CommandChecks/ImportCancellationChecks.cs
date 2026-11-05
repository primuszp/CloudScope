using CloudScope.Loading;

internal static class ImportCancellationChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "scan.xyz"), "0 0 0\n1 1 1\n");
            File.WriteAllText(Path.Combine(directory, "scan.pts"), "2\n0 0 0\n1 1 1\n");
            File.WriteAllText(Path.Combine(directory, "scan.ply"), "ply\nformat ascii 1.0\nelement vertex 2\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n1 1 1\n");
            File.WriteAllText(Path.Combine(directory, "scan.ptx"), "1\n2\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n1 0 0 0\n0 1 0 0\n0 0 1 0\n0 0 0 1\n1 2 3 1\n2 3 4 1\n");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "two-scans.e57"), Path.Combine(directory, "scan.e57"));
            foreach ((string extension, Func<string, long, IProgress<int>?, CancellationToken, LoadedPointCloud> load) in new (string, Func<string, long, IProgress<int>?, CancellationToken, LoadedPointCloud>)[]
            {
                ("xyz", XyzPointCloudLoader.Load), ("pts", PtsPointCloudLoader.Load),
                ("ply", PlyPointCloudLoader.Load), ("ptx", PtxPointCloudLoader.Load), ("e57", E57PointCloudLoader.Load)
            })
            {
                bool rejected = false;
                try { load(Path.Combine(directory, "absent." + extension), 0, null, new CancellationToken(true)); }
                catch (OperationCanceledException) { rejected = true; }
                check($"{extension.ToUpperInvariant()} honours pre-cancellation before opening input", rejected, "");
                using var cancellation = new CancellationTokenSource();
                bool cancelled = false;
                string path = Path.Combine(directory, "scan." + extension);
                try { load(path, 0, new CancellationProgress(_ => cancellation.Cancel()), cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                check($"{extension.ToUpperInvariant()} cancels during import instead of returning a partial cloud", cancelled, "");
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                check($"{extension.ToUpperInvariant()} releases its input after cancellation", exclusive.CanRead, "");
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}

internal sealed class CancellationProgress(Action<int> report) : IProgress<int>
{
    public void Report(int value) => report(value);
}
