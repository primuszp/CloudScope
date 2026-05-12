using System;

namespace CloudScope.Rendering
{
    public interface IRenderFrameSession : IDisposable
    {
        IRenderFrameData FrameData { get; }
    }
}
