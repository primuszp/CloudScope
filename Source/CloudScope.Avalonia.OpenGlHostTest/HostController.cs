using Avalonia.OpenGL;
using CloudScope;

namespace CloudScope.Avalonia.OpenGlHostTest;

public sealed class HostController
{
    private DateTime _startTimeUtc = DateTime.UtcNow;
    private int _frameCount;
    private string _renderer = "OpenGL context not initialized";
    private readonly object _pendingLock = new();
    private readonly AvaloniaPointCloudRenderer _pointRenderer = new();
    private readonly AvaloniaCameraController _camera = new();
    private PendingCloud? _pendingCloud;
    private int _renderedPointCount;
    private bool _glInitialized;
    private float _lastCloudRadius = 1f;

    public event Action<string>? StatusChanged;

    public string Status => $"Frames: {_frameCount:N0} | Points: {_renderedPointCount:N0} | {_renderer}";

    public void SetPendingCloud(PointData[] points, int count, float radius, string sourceName)
    {
        lock (_pendingLock)
            _pendingCloud = new PendingCloud(points, count, radius, sourceName);

        StatusChanged?.Invoke($"Queued cloud upload: {sourceName} ({count:N0} points)");
    }

    public void Initialize(GlInterface gl)
    {
        _startTimeUtc = DateTime.UtcNow;
        _frameCount = 0;
        _renderer = $"{gl.Vendor} | {gl.Renderer} | {gl.Version}";
        _pointRenderer.Initialize(OpenGlApi.Get(gl));
        _glInitialized = true;
        PublishStatus();
    }

    public void Render(GlInterface gl, int framebuffer, int pixelWidth, int pixelHeight)
    {
        double t = (DateTime.UtcNow - _startTimeUtc).TotalSeconds;
        float r = 0.08f + 0.05f * (float)Math.Sin(t * 0.9);
        float g = 0.10f + 0.04f * (float)Math.Sin(t * 1.3 + 1.2);
        float b = 0.14f + 0.04f * (float)Math.Sin(t * 1.7 + 2.1);

        OpenGlApi api = OpenGlApi.Get(gl);
        UploadPendingCloud(api);

        api.BindFramebuffer(OpenGlApi.Framebuffer, framebuffer);
        api.Viewport(0, 0, pixelWidth, pixelHeight);
        api.ClearColor(_renderedPointCount > 0 ? 0.02f : r, _renderedPointCount > 0 ? 0.025f : g, _renderedPointCount > 0 ? 0.035f : b, 1f);
        api.Clear(OpenGlApi.ColorBufferBit | OpenGlApi.DepthBufferBit);
        if (_renderedPointCount > 0)
            _pointRenderer.Render(api, _camera.CreateMvp(pixelWidth, pixelHeight), _camera.PointSize);
        api.Flush();

        _frameCount++;
        if ((_frameCount % 30) == 0)
            PublishStatus();
    }

    public void Deinitialize(GlInterface gl)
    {
        if (_glInitialized)
            _pointRenderer.DisposeGl(OpenGlApi.Get(gl));
        _glInitialized = false;
        _renderer = "OpenGL context disposed";
        PublishStatus();
    }

    public string ExecuteCommand(string commandText)
    {
        string command = commandText.Trim().ToUpperInvariant();
        string result = command switch
        {
            "" => "",
            "STATUS" => Status,
            "RESET" => Reset(),
            "HELP" or "?" => "Commands: STATUS, RESET, HELP. Use Host/Open LAS from the menu to load a cloud.",
            _ => $"Unknown command: {commandText.Trim()}"
        };

        if (result.Length > 0)
            StatusChanged?.Invoke(result);

        return result;
    }

    private string Reset()
    {
        _startTimeUtc = DateTime.UtcNow;
        _frameCount = 0;
        PublishStatus();
        return "Host controller reset";
    }

    private void PublishStatus() => StatusChanged?.Invoke(Status);

    private void UploadPendingCloud(OpenGlApi api)
    {
        PendingCloud? cloud;
        lock (_pendingLock)
        {
            cloud = _pendingCloud;
            _pendingCloud = null;
        }

        if (cloud == null)
            return;

        _pointRenderer.Upload(api, cloud.Points, cloud.Count, cloud.Radius);
        _lastCloudRadius = Math.Max(cloud.Radius, 1f);
        _camera.Fit(cloud.Radius);
        _renderedPointCount = cloud.Count;
        StatusChanged?.Invoke($"Uploaded cloud: {cloud.SourceName} ({cloud.Count:N0} points)");
    }

    public void Orbit(float dx, float dy) => _camera.Orbit(dx, dy);

    public void Pan(float dx, float dy, int viewportHeight) => _camera.Pan(dx, dy, viewportHeight);

    public void Zoom(float wheelDelta) => _camera.Zoom(wheelDelta);

    public void AdjustPointSize(float delta) => _camera.AdjustPointSize(delta);

    public void ResetCamera()
    {
        _camera.Fit(_lastCloudRadius);
        PublishStatus();
    }

    private sealed record PendingCloud(PointData[] Points, int Count, float Radius, string SourceName);
}
