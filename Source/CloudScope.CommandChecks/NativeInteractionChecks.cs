using CloudScope;
using CloudScope.Ui;

internal static class NativeInteractionChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        using var viewer = new ViewerController(1280, 720, new HeadlessBackend());
        viewer.Load();
        var commands = new ViewerCommandDispatcher(viewer);
        var keyboard = new ViewerKeyboardState();
        foreach (string name in new[] { "Top", "Bottom", "Front", "Back", "Left", "Right", "Isometric" })
        {
            commands.Execute("VIEW " + name);
            check($"On-demand view {name} requests animation frames", viewer.NeedsContinuousFrames, "");
            int frames = 0;
            while (viewer.NeedsContinuousFrames && frames++ < 120) viewer.UpdateFrame(1f / 60, keyboard);
            check($"On-demand view {name} reaches its real orientation and settles", frames < 120 && viewer.Status.ViewName == name && !viewer.NeedsContinuousFrames, viewer.Status.ViewName);
        }
        commands.Execute("VPORTS Four Top");
        commands.Execute("VIEW Front");
        for (int frame = 0; frame < 120 && viewer.NeedsContinuousFrames; frame++) viewer.UpdateFrame(1f / 60, keyboard);
        check("Split-viewport view label follows the active camera", viewer.Status.ViewName == "Front", viewer.Status.ViewName);
        commands.Execute("VPORTS Single Top");
        keyboard.KeyDown(ViewerKey.W);
        viewer.UpdateFrame(1f / 60, keyboard);
        check("Held navigation keeps an on-demand viewport rendering", viewer.NeedsContinuousFrames, "");
        keyboard.KeyUp(ViewerKey.W);
        viewer.UpdateFrame(1f / 60, keyboard);
        check("Navigation release returns the viewport to idle", !viewer.NeedsContinuousFrames, "");

        var camera = new OrbitCamera();
        camera.SetViewportSize(1280, 720);
        camera.FitToCloud(10);
        var input = new CameraInputController();
        var keys = new ViewerKeyboardState();
        keys.KeyDown(ViewerKey.W);
        var before = camera.SaveState();
        input.UpdateFrame(3600, keys, camera, 10, false, 1280, 720, false);
        var after = camera.SaveState();
        check("First input after a long idle period cannot jump across the scene", (after.Translation - before.Translation).Length < 100, "");
        before = camera.SaveState();
        input.UpdateFrame(1f / 60, keys, camera, 10, false, 1280, 720, false, acceptsKeyboard: false);
        check("Inactive viewports do not consume keyboard navigation", camera.SaveState().Translation == before.Translation && !input.NeedsContinuousFrames, "");
        camera.SetTopView();
        camera.TickTransition(1);
        camera.OrbitBy(-720, 0);
        check("View names handle complete negative rotations", camera.ViewName == "Top", camera.ViewName);
        camera.SetFrontView();
        camera.OrbitBy(10, 0);
        check("Explicit orbit cancels an older view transition", !camera.IsTransitioning && camera.ViewName == "Custom", camera.ViewName);
    }
}
