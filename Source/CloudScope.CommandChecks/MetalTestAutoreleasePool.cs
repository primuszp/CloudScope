#if ENABLE_SHARPMETAL
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("macos")]
internal sealed class MetalTestAutoreleasePool : IDisposable
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPush")] private static extern IntPtr Push();
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPop")] private static extern void Pop(IntPtr pool);
    private readonly IntPtr _pool = Push();
    public void Dispose() => Pop(_pool);
}
#endif
