using CloudScope.Loading;
using CloudScope.Projects;

namespace CloudScope;

/// <summary>One recovery snapshot per viewer. A lock keeps other running viewers' work out of the recovery list.</summary>
internal sealed class ProjectRecovery : IDisposable
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudScope", "Recovery");
    private readonly string _path;
    private FileStream? _lock;
    private Task _write = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    private DateTime _next = DateTime.UtcNow.AddSeconds(30);
    private string _writtenSignature = "";
    private bool _disposed;
    internal ProjectRecovery(string? directory = null)
    { _path = Path.Combine(directory ?? DirectoryPath, Guid.NewGuid().ToString("N") + ".cloudscope"); }
    internal string PathName => _path;
    internal Task Completion => _write;
    public static bool IsRecovery(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public static string[] Available()
    {
        if (!Directory.Exists(DirectoryPath)) return [];
        return Directory.GetFiles(DirectoryPath, "*.cloudscope").Where(path =>
        {
            try { using var probe = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
    }
    public void Tick(Func<bool> dirty, Func<string> signature,
        Func<(ProjectState State, PointCloudDataset? Dataset, SurfaceMesh? Surface)> capture, Action<string> message, bool force = false)
    {
        if (_disposed || !_write.IsCompleted || (!force && DateTime.UtcNow < _next)) return;
        _next = DateTime.UtcNow.AddSeconds(30);
        if (!dirty()) return;
        string stamp = signature();
        if (stamp == _writtenSignature) return;
        var snapshot = capture();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            _lock ??= new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var cancellation = _cancellation.Token;
            _write = Task.Run(() =>
            {
                try { ProjectFile.Save(_path, snapshot.State, snapshot.Dataset, snapshot.Surface, cancellation); _writtenSignature = stamp; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { message($"Recovery snapshot could not be written: {ex.Message}"); }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { message($"Recovery snapshot could not be written: {ex.Message}"); }
    }
    public void Clear()
    {
        _cancellation?.Cancel();
        _write.GetAwaiter().GetResult();
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _writtenSignature = ""; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // An orderly close has already passed the document guard. A process crash leaves this file for RECOVER.
        try { Clear(); }
        finally
        {
            _cancellation?.Dispose(); _lock?.Dispose(); try { if (File.Exists(_path + ".lock")) File.Delete(_path + ".lock"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
