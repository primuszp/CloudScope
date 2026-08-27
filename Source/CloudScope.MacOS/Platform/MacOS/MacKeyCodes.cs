using System.Runtime.Versioning;

namespace CloudScope.Platform.MacOS
{
    /// <summary>
    /// Translates AppKit virtual key codes (<c>NSEvent.keyCode</c>) into <see cref="ViewerKey"/>.
    /// Shared by every AppKit-backed host so the standalone and embedded Metal windows
    /// answer to the same keys.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static class MacKeyCodes
    {
        public static ViewerKey ToViewerKey(ushort keyCode) => keyCode switch
        {
            53 => ViewerKey.Escape,
            49 => ViewerKey.Space,
            36 => ViewerKey.Enter,
            56 => ViewerKey.LeftShift,
            60 => ViewerKey.RightShift,
            59 => ViewerKey.LeftControl,
            62 => ViewerKey.RightControl,
            12 => ViewerKey.Q,
            13 => ViewerKey.W,
            14 => ViewerKey.E,
            0 => ViewerKey.A,
            1 => ViewerKey.S,
            2 => ViewerKey.D,
            3 => ViewerKey.F,
            69 => ViewerKey.KeyPadAdd,
            78 => ViewerKey.KeyPadSubtract,
            71 => ViewerKey.KeyPad7,
            77 => ViewerKey.KeyPad3,
            65 => ViewerKey.KeyPad1,
            87 => ViewerKey.KeyPad5,
            115 => ViewerKey.Home,
            _ => ViewerKey.Unknown
        };
    }
}
