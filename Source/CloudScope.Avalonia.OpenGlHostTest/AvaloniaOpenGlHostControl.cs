using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;

namespace CloudScope.Avalonia.OpenGlHostTest;

public sealed class AvaloniaOpenGlHostControl : OpenGlControlBase
{
    public HostController? HostController { get; set; }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        HostController?.Initialize(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        HostController?.Render(gl, fb, width, height);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        HostController?.Deinitialize(gl);
    }
}
