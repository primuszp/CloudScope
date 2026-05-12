namespace CloudScope.Rendering
{
    public interface IRenderFrameLifecycle
    {
        void InitializeFrameState();
        IRenderFrameSession BeginFrame();
        void Resize(int width, int height);
    }
}
