using OpenTK.Windowing.GraphicsLibraryFramework;
using CloudScope.Rendering;

namespace CloudScope
{
    public readonly struct OpenTkKeyboardAdapter : IViewerKeyboard
    {
        private readonly KeyboardState _keyboard;

        public OpenTkKeyboardAdapter(KeyboardState keyboard)
        {
            _keyboard = keyboard;
        }

        public bool IsKeyPressed(ViewerKey key) => _keyboard.IsKeyPressed(ToOpenTkKey(key));

        public bool IsKeyDown(ViewerKey key) => _keyboard.IsKeyDown(ToOpenTkKey(key));

        public static Keys ToOpenTkKey(ViewerKey key) => key switch
        {
            ViewerKey.Unknown => Keys.Unknown,
            ViewerKey.Escape => Keys.Escape,
            ViewerKey.KeyPadAdd => Keys.KeyPadAdd,
            ViewerKey.KeyPadSubtract => Keys.KeyPadSubtract,
            ViewerKey.KeyPad1 => Keys.KeyPad1,
            ViewerKey.KeyPad3 => Keys.KeyPad3,
            ViewerKey.KeyPad5 => Keys.KeyPad5,
            ViewerKey.KeyPad7 => Keys.KeyPad7,
            ViewerKey.Home => Keys.Home,
            ViewerKey.LeftShift => Keys.LeftShift,
            ViewerKey.RightShift => Keys.RightShift,
            ViewerKey.W => Keys.W,
            ViewerKey.A => Keys.A,
            ViewerKey.S => Keys.S,
            ViewerKey.D => Keys.D,
            ViewerKey.Q => Keys.Q,
            ViewerKey.E => Keys.E,
            ViewerKey.O => Keys.O,
            ViewerKey.L => Keys.L,
            ViewerKey.Enter => Keys.Enter,
            ViewerKey.G => Keys.G,
            ViewerKey.R => Keys.R,
            ViewerKey.X => Keys.X,
            ViewerKey.Y => Keys.Y,
            ViewerKey.Z => Keys.Z,
            ViewerKey.D1 => Keys.D1,
            ViewerKey.D2 => Keys.D2,
            ViewerKey.D3 => Keys.D3,
            ViewerKey.D4 => Keys.D4,
            ViewerKey.D5 => Keys.D5,
            ViewerKey.D6 => Keys.D6,
            ViewerKey.D7 => Keys.D7,
            ViewerKey.D8 => Keys.D8,
            ViewerKey.D9 => Keys.D9,
            ViewerKey.Space => Keys.Space,
            ViewerKey.F => Keys.F,
            ViewerKey.LeftControl => Keys.LeftControl,
            ViewerKey.RightControl => Keys.RightControl,
            _ => Keys.Unknown
        };
    }
}
