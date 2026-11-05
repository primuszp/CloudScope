namespace CloudScope.Rendering
{
    public interface IRenderFrameLifecycle
    {
        string ResourceDiagnostics => "";
        void Initialize();
        void WaitForIdle() { }
        void DisposeFrameResources() { }
        IRenderFrameSession BeginFrame();
        void Resize(int width, int height);
        void SetViewport(int x, int y, int width, int height);
    }
}
