namespace CloudScope.Rendering
{
    public sealed class EmptyRenderFrameData : IRenderFrameData
    {
        public static readonly EmptyRenderFrameData Instance = new();

        private EmptyRenderFrameData()
        {
        }
    }
}
