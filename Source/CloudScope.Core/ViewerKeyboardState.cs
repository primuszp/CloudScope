using System.Collections.Generic;

namespace CloudScope
{
    /// <summary>
    /// Keyboard state fed by discrete key events, for hosts that receive key up/down
    /// callbacks instead of polling a device (the Metal/AppKit hosts).
    /// <see cref="IsKeyPressed"/> consumes the press, so a held key reports a single
    /// press just like the polled OpenTK adapter does.
    /// </summary>
    public sealed class ViewerKeyboardState : IViewerKeyboard
    {
        private readonly HashSet<ViewerKey> _down = [];
        private readonly HashSet<ViewerKey> _pressed = [];

        public bool HasAnyKeyDown => _down.Count > 0;

        public void KeyDown(ViewerKey key)
        {
            if (_down.Add(key))
                _pressed.Add(key);
        }

        public void KeyUp(ViewerKey key)
        {
            _down.Remove(key);
            _pressed.Remove(key);
        }

        public bool IsKeyDown(ViewerKey key) => _down.Contains(key);

        public bool IsKeyPressed(ViewerKey key) => _pressed.Remove(key);
    }
}
