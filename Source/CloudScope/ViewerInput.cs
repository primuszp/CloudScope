namespace CloudScope
{
    public enum ViewerMouseButton
    {
        Left,
        Right,
        Middle
    }

    public enum ViewerKey
    {
        Unknown,
        Escape,
        KeyPadAdd,
        KeyPadSubtract,
        KeyPad1,
        KeyPad3,
        KeyPad5,
        KeyPad7,
        Home,
        LeftShift,
        RightShift,
        W,
        A,
        S,
        D,
        Q,
        E,
        O,
        L,
        Enter,
        G,
        R,
        X,
        Y,
        Z,
        D1,
        D2,
        D3,
        D4,
        D5,
        D6,
        D7,
        D8,
        D9,
        Space,
        F,
        LeftControl,
        RightControl
    }

    public interface IViewerKeyboard
    {
        bool IsKeyPressed(ViewerKey key);
        bool IsKeyDown(ViewerKey key);
    }
}
