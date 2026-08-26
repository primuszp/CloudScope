namespace CloudScope.Rendering
{
    public interface IRenderFrameLifecycle
    {
        void Initialize();
        IRenderFrameSession BeginFrame();
        void Resize(int width, int height);
        void SetViewport(int x, int y, int width, int height);
    }
}
