using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace CloudScope;

/// <summary>Owns camera histories and named views; has no document, renderer or window dependency.</summary>
internal sealed class ViewNavigation
{
    private readonly ConditionalWeakTable<OrbitCamera, List<OrbitCamera.CameraState>> _history = new();
    private readonly Dictionary<string, OrbitCamera.CameraState> _named = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, OrbitCamera.CameraState> Capture() => _named.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    internal void Replace(IReadOnlyDictionary<string, OrbitCamera.CameraState> views)
    { _named.Clear(); foreach (var pair in views) _named.Add(pair.Key, pair.Value); _history.Clear(); }
    public string[] NamedViews => _named.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    public void Remember(OrbitCamera camera)
    {
        var history = _history.GetOrCreateValue(camera);
        if (history.Count == 20) history.RemoveAt(0);
        history.Add(camera.SaveState());
    }
    public bool Previous(OrbitCamera camera)
    {
        var history = _history.GetOrCreateValue(camera);
        if (history.Count == 0) return false;
        var state = history[^1]; history.RemoveAt(history.Count - 1);
        camera.RestoreState(state, animate: false);
        return true;
    }
    public void ZoomByFactor(OrbitCamera camera, int width, int height, float factor)
    {
        Remember(camera);
        camera.PickDepthWindow(width / 2, height / 2, 11);
        camera.Zoom(width / 2, height / 2, factor);
    }
    public void ZoomWindow(OrbitCamera camera, int x0, int y0, int x1, int y1)
    { Remember(camera); camera.ZoomWindow(x0, y0, x1, y1); }
    public bool ZoomCenter(OrbitCamera camera, int x, int y, float height)
    {
        if (height <= 0) return false;
        Remember(camera); return camera.ZoomCenter(x, y, height);
    }
    public string PanByPixels(OrbitCamera camera, int dx, int dy)
    { Remember(camera); camera.PanPixels(dx, dy); return $"Panned {dx},{dy} pixels."; }
    public string PanBetweenPoints(OrbitCamera camera, int x0, int y0, int x1, int y1)
    { Remember(camera); camera.PickDepthWindow(x0, y0, 11); camera.Pan(x0, y0, x1, y1); return "Panned."; }
    public string OrbitBy(OrbitCamera camera, float azimuth, float elevation)
    { camera.OrbitBy(azimuth, elevation); return $"Orbited {azimuth:0.###}°, {elevation:0.###}°."; }
    public string SetPivot(OrbitCamera camera, Vector3 point)
    { camera.SetOrbitPivot(point); return $"Pivot: {point.X:0.###},{point.Y:0.###},{point.Z:0.###}"; }
    public string SetPivotFromScreen(OrbitCamera camera, int x, int y)
        => camera.SetOrbitPivotFromScreen(x, y) ? $"Pivot: {camera.Pivot.X:0.###},{camera.Pivot.Y:0.###},{camera.Pivot.Z:0.###}" : "No point under that position.";
    public string ResetPivot(OrbitCamera camera)
    { camera.SetOrbitPivot(Vector3.Zero); return "Pivot reset to the cloud origin."; }
    public string Save(OrbitCamera camera, string name)
    { _named[name] = camera.SaveState(); return $"View '{name}' saved."; }
    public string Restore(OrbitCamera camera, string name)
    {
        if (!_named.TryGetValue(name, out var state)) return $"No saved view named '{name}'.";
        camera.RestoreState(state); return $"View '{name}' restored.";
    }
    public string Delete(string name) => _named.Remove(name) ? $"View '{name}' deleted." : $"No saved view named '{name}'.";
    public string Describe() => _named.Count == 0 ? "No saved views. Store one with VIEW Save." : "Saved views: " + string.Join(", ", NamedViews);
}
