using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Metal.NET;

[SupportedOSPlatform("macos")]
static unsafe class Program
{
    const string LibObjC = "/usr/lib/libobjc.dylib";

    // ── ObjC runtime P/Invoke ─────────────────────────────────────────────────────
    [DllImport("/usr/lib/libdl.dylib")] static extern nint dlopen(string path, int mode);
    [DllImport(LibObjC)] static extern nint objc_getClass(string name);
    [DllImport(LibObjC)] static extern nint sel_registerName(string name);
    [DllImport(LibObjC)] static extern nint objc_allocateClassPair(nint super, byte* name, nuint extra);
    [DllImport(LibObjC)] static extern bool class_addMethod(nint cls, nint sel, nint imp, byte* types);
    [DllImport(LibObjC)] static extern void objc_registerClassPair(nint cls);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern nint  Msg0(nint r, nint s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern nint  Msg1(nint r, nint s, nint a1);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern nint  MsgInitWindow(nint r, nint s, NSRect frame, ulong style, ulong backing, byte defer_);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern nint  MsgInitView(nint r, nint s, NSRect frame, nint device);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern void  MsgVoid0(nint r, nint s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern void  MsgVoid1(nint r, nint s, nint a1);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern void  MsgVoidByte(nint r, nint s, byte a1);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern void  MsgVoidLong(nint r, nint s, long a1);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern void  MsgVoidULong(nint r, nint s, ulong a1);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] static extern void  MsgVoidClearColor(nint r, nint s, MTLClearColor c);

    // ── Value types passed to ObjC ────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    struct NSRect { public double X, Y, W, H; public NSRect(double x, double y, double w, double h) { X=x; Y=y; W=w; H=h; } }

    // ── Static delegates (GC must not collect them) ────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void NotifCb(nint id, nint cmd, nint notif);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DrawCb(nint id, nint cmd, nint view);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SizeCb(nint id, nint cmd, nint view, CGSize size);

    static NotifCb? s_didFinish;
    static DrawCb?  s_draw;
    static SizeCb?  s_sizeChange;
    static Action?  s_onLaunched;
    static Action<nint>? s_onDraw;

    // ── Long-lived Metal resources ────────────────────────────────────────────────
    static MTLDevice?              s_device;
    static MTLCommandQueue?        s_queue;
    static MTLRenderPipelineState? s_pipeline;

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Out.Flush();
        // unbuffered output so logs appear immediately even when piped
        var writer = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(writer);

        dlopen("/System/Library/Frameworks/AppKit.framework/AppKit",    9);
        dlopen("/System/Library/Frameworks/MetalKit.framework/MetalKit", 9);
        dlopen("/System/Library/Frameworks/Metal.framework/Metal",       9);

        nint app = Msg0(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        MsgVoidLong(app, sel_registerName("setActivationPolicy:"), 0); // NSApplicationActivationPolicyRegular

        nint appDelegateCls = BuildAppDelegateClass();
        nint appDelegate    = Msg0(Msg0(appDelegateCls, sel_registerName("alloc")), sel_registerName("init"));
        s_onLaunched = () => Setup(app);
        MsgVoid1(app, sel_registerName("setDelegate:"), appDelegate);

        Console.WriteLine("[Boot] Starting NSApplication.run()...");
        MsgVoid0(app, sel_registerName("run"));
    }

    // ── App delegate ObjC class ───────────────────────────────────────────────────
    static nint BuildAppDelegateClass()
    {
        s_didFinish = static (id, cmd, notif) => s_onLaunched?.Invoke();

        byte[] name  = Encoding.UTF8.GetBytes("MetalNetAppDelegate\0");
        byte[] types = Encoding.UTF8.GetBytes("v@:@\0");

        nint cls;
        fixed (byte* n = name) fixed (byte* t = types)
        {
            cls = objc_allocateClassPair(objc_getClass("NSObject"), n, 0);
            class_addMethod(cls, sel_registerName("applicationDidFinishLaunching:"),
                Marshal.GetFunctionPointerForDelegate(s_didFinish), t);
            objc_registerClassPair(cls);
        }
        return cls;
    }

    // ── MTKView delegate ObjC class ───────────────────────────────────────────────
    static nint BuildMTKDelegateClass()
    {
        s_draw       = static (id, cmd, view) => s_onDraw?.Invoke(view);
        s_sizeChange = static (id, cmd, view, size) => { }; // ignored — MTKView handles it

        byte[] name      = Encoding.UTF8.GetBytes("MetalNetMTKDelegate\0");
        byte[] drawTypes = Encoding.UTF8.GetBytes("v@:@\0");
        byte[] sizeTypes = Encoding.UTF8.GetBytes("v@:@{CGSize=dd}\0");

        nint cls;
        fixed (byte* n = name) fixed (byte* dt = drawTypes) fixed (byte* st = sizeTypes)
        {
            cls = objc_allocateClassPair(objc_getClass("NSObject"), n, 0);
            class_addMethod(cls, sel_registerName("drawInMTKView:"),
                Marshal.GetFunctionPointerForDelegate(s_draw), dt);
            class_addMethod(cls, sel_registerName("mtkView:drawableSizeWillChange:"),
                Marshal.GetFunctionPointerForDelegate(s_sizeChange), st);
            objc_registerClassPair(cls);
        }

        nint alloc = Msg0(cls, sel_registerName("alloc"));
        return Msg0(alloc, sel_registerName("init"));
    }

    // ── Main setup (runs after app launched) ──────────────────────────────────────
    static void Setup(nint app)
    {
        s_device   = MTLDevice.CreateSystemDefaultDevice();
        s_queue    = s_device.MakeCommandQueue();
        s_pipeline = BuildPipeline(s_device);
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[Init] device={s_device.Name}  pipeline={(s_pipeline.IsNull ? "FAIL" : "OK")}");

        // ── Window ────────────────────────────────────────────────────────────────
        var frame = new NSRect(150, 150, 800, 600);
        nint winAlloc = Msg0(objc_getClass("NSWindow"), sel_registerName("alloc"));
        nint win = MsgInitWindow(winAlloc,
            sel_registerName("initWithContentRect:styleMask:backing:defer:"),
            frame, 1 | 2 | 4 | 8, 2, 0);

        using var title = (NSString)"Metal.NET - spinning triangle";
        MsgVoid1(win, sel_registerName("setTitle:"), title.NativePtr);

        // ── MTKView ───────────────────────────────────────────────────────────────
        nint mtkAlloc = Msg0(objc_getClass("MTKView"), sel_registerName("alloc"));
        nint mtk = MsgInitView(mtkAlloc,
            sel_registerName("initWithFrame:device:"),
            frame, s_device.NativePtr);

        MsgVoidULong(mtk, sel_registerName("setColorPixelFormat:"), (ulong)MTLPixelFormat.BGRA8Unorm);
        MsgVoidClearColor(mtk, sel_registerName("setClearColor:"), new MTLClearColor(0.07, 0.07, 0.15, 1.0));
        MsgVoidByte(mtk, sel_registerName("setPaused:"),                   0); // continuous rendering
        MsgVoidByte(mtk, sel_registerName("setEnableSetNeedsDisplay:"),    0);
        MsgVoidByte(mtk, sel_registerName("setFramebufferOnly:"),          0);
        MsgVoidLong(mtk, sel_registerName("setPreferredFramesPerSecond:"), 60);

        // ── Draw callback ─────────────────────────────────────────────────────────
        int frameCount = 0;
        s_onDraw = view =>
        {
            using var pool = new NSAutoreleasePool();

            nint descPtr = Msg0(view, sel_registerName("currentRenderPassDescriptor"));
            nint drawPtr = Msg0(view, sel_registerName("currentDrawable"));
            if (descPtr == 0 || drawPtr == 0) return;

            // Borrowed — lifetime managed by ObjC autorelease pool
            var desc     = MTLRenderPassDescriptor.New(descPtr, NativeObjectOwnership.Borrowed);
            var drawable = CAMetalDrawable.New(drawPtr,         NativeObjectOwnership.Borrowed);

            using var cmdBuf = s_queue!.MakeCommandBuffer();
            if (cmdBuf.IsNull) return;

            using var encoder = cmdBuf.MakeRenderCommandEncoder(desc);
            if (encoder.IsNull) { cmdBuf.Commit(); return; }

            float t = (float)sw.Elapsed.TotalSeconds;
            encoder.SetRenderPipelineState(s_pipeline!);
            encoder.SetVertexBytes((nint)(&t), (nuint)sizeof(float), 0);
            encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);

            encoder.EndEncoding();
            cmdBuf.Present(drawable);
            cmdBuf.Commit();

            frameCount++;
            if (frameCount <= 3)
                Console.WriteLine($"[F{frameCount}] t={t:F2}s");
        };

        nint mtkDelegate = BuildMTKDelegateClass();
        MsgVoid1(mtk, sel_registerName("setDelegate:"), mtkDelegate);
        MsgVoid1(win, sel_registerName("setContentView:"), mtk);

        // ── Show window ───────────────────────────────────────────────────────────
        MsgVoid1(win, sel_registerName("makeKeyAndOrderFront:"), 0);
        MsgVoidByte(app, sel_registerName("activateIgnoringOtherApps:"), 1);
        Console.WriteLine("[Init] Window shown — spinning triangle active");
    }

    // ── Metal pipeline ────────────────────────────────────────────────────────────
    const string ShaderSrcSimple = """
        #include <metal_stdlib>
        using namespace metal;

        struct VOut { float4 pos [[position]]; float4 col; };

        vertex VOut vert_main(uint vid [[vertex_id]],
                              constant float& time [[buffer(0)]]) {
            float2 pos[3]    = { {0.0, 0.55}, {-0.5, -0.38}, {0.5, -0.38} };
            float4 col[3]    = { {1,0.2,0.1,1}, {0.1,1,0.3,1}, {0.2,0.4,1,1} };
            float c = cos(time), s = sin(time);
            float2 p = pos[vid];
            float2 r = float2(c*p.x - s*p.y, s*p.x + c*p.y);
            VOut o; o.pos = float4(r, 0, 1); o.col = col[vid]; return o;
        }

        fragment float4 frag_main(VOut in [[stage_in]]) { return in.col; }
        """;

    static MTLRenderPipelineState BuildPipeline(MTLDevice device)
    {
        var lib = device.MakeLibrary(ShaderSrcSimple, new MTLCompileOptions(), out NSError libErr);
        if (lib.IsNull)
        {
            Console.WriteLine($"[Shader] compile error: {(NSString)libErr.LocalizedDescription}");
            return MTLRenderPipelineState.Null;
        }

        using var vert = lib.MakeFunction("vert_main");
        using var frag = lib.MakeFunction("frag_main");

        if (vert.IsNull || frag.IsNull)
        {
            Console.WriteLine("[Shader] function lookup failed");
            return MTLRenderPipelineState.Null;
        }

        var desc = new MTLRenderPipelineDescriptor();
        desc.VertexFunction   = vert;
        desc.FragmentFunction = frag;
        desc.ColorAttachments[(nuint)0].PixelFormat = MTLPixelFormat.BGRA8Unorm;

        var state = device.MakeRenderPipelineState(desc, out NSError pipeErr);
        if (!pipeErr.IsNull)
            Console.WriteLine($"[Pipeline] error: {(NSString)pipeErr.LocalizedDescription}");

        return state;
    }
}
