using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope
{
    public sealed class CameraInputController
    {
        private int _lastX, _lastY;
        private bool _leftDown, _rightDown, _middleDown;
        private float _orbitVelX, _orbitVelY;
        private float _panVelX, _panVelY;
        private float _pointSize = 1.5f;
        private float _pivotFade;
        private float _pivotFlash;
        private Vector3 _displayPivot = Vector3.Zero;

        public float PointSize => _pointSize;
        public float PivotFade => _pivotFade;
        public float PivotFlash => _pivotFlash;
        public Vector3 DisplayPivot => _displayPivot;

        public bool UpdateFrame(
            float dt,
            IViewerKeyboard keyboard,
            OrbitCamera camera,
            float cloudRadius,
            bool selectionVolumeActive,
            int viewportWidth,
            int viewportHeight)
        {
            bool shouldClose = keyboard.IsKeyPressed(ViewerKey.Escape);

            if (keyboard.IsKeyPressed(ViewerKey.KeyPadAdd))
                _pointSize = MathF.Min(_pointSize + 0.5f, 20f);
            if (keyboard.IsKeyPressed(ViewerKey.KeyPadSubtract))
                _pointSize = MathF.Max(_pointSize - 0.5f, 0.5f);

            if (keyboard.IsKeyPressed(ViewerKey.KeyPad1)) camera.SetFrontView();
            if (keyboard.IsKeyPressed(ViewerKey.KeyPad3)) camera.SetRightView();
            if (keyboard.IsKeyPressed(ViewerKey.KeyPad7)) camera.SetTopView();
            if (keyboard.IsKeyPressed(ViewerKey.KeyPad5)) camera.SetIsometric();
            if (keyboard.IsKeyPressed(ViewerKey.Home)) camera.ResetView(cloudRadius);

            float moveSpeed = camera.NavigationScale * 2.0f * dt;
            if (keyboard.IsKeyDown(ViewerKey.LeftShift) || keyboard.IsKeyDown(ViewerKey.RightShift))
                moveSpeed *= 5f;

            float dx = 0, dy = 0, dz = 0;
            if (keyboard.IsKeyDown(ViewerKey.W)) dz -= moveSpeed;
            if (keyboard.IsKeyDown(ViewerKey.S)) dz += moveSpeed;
            if (keyboard.IsKeyDown(ViewerKey.A)) dx -= moveSpeed;
            if (keyboard.IsKeyDown(ViewerKey.D)) dx += moveSpeed;
            if (keyboard.IsKeyDown(ViewerKey.E)) dy += moveSpeed;
            if (keyboard.IsKeyDown(ViewerKey.Q)) dy -= moveSpeed;

            if (dx != 0 || dy != 0 || dz != 0)
            {
                camera.CancelTransition();
                camera.MoveFPS(dx, dy, dz);
            }

            camera.TickTransition(dt);
            UpdatePivotAnimation(dt, selectionVolumeActive);
            UpdateInertia(camera, viewportWidth, viewportHeight);
            return shouldClose;
        }

        public void MouseDown(
            ViewerMouseButton button,
            int x,
            int y,
            OrbitCamera camera,
            bool leftButtonConsumed,
            bool activeSelectionVolume,
            Vector3 activeSelectionCenter)
        {
            if (button == ViewerMouseButton.Left && !leftButtonConsumed)
            {
                if (activeSelectionVolume)
                {
                    camera.SetOrbitPivot(activeSelectionCenter);
                    _displayPivot = activeSelectionCenter;
                }
                else if (camera.SetOrbitPivotFromScreen(x, y, 11))
                {
                    _displayPivot = camera.Pivot;
                    _pivotFlash = 1.0f;
                }

                _leftDown = true;
                _orbitVelX = 0f;
                _orbitVelY = 0f;
            }

            if (button == ViewerMouseButton.Right)
            {
                camera.PickDepthWindow(x, y, 11);
                _rightDown = true;
                _panVelX = 0f;
                _panVelY = 0f;
            }

            if (button == ViewerMouseButton.Middle)
            {
                camera.PickDepthWindow(x, y, 11);
                _middleDown = true;
                _panVelX = 0f;
                _panVelY = 0f;
            }

            _lastX = x;
            _lastY = y;
        }

        public void MouseUp(ViewerMouseButton button)
        {
            if (button == ViewerMouseButton.Left) _leftDown = false;
            if (button == ViewerMouseButton.Right) _rightDown = false;
            if (button == ViewerMouseButton.Middle) _middleDown = false;
        }

        public void MouseMove(int x, int y, OrbitCamera camera)
        {
            int dx = x - _lastX;
            int dy = y - _lastY;

            if (_leftDown)
            {
                camera.Rotate(dx, dy);
                _orbitVelX = dx;
                _orbitVelY = dy;
            }
            else if (_rightDown || _middleDown)
            {
                camera.Pan(_lastX, _lastY, x, y);
                _panVelX = x - _lastX;
                _panVelY = y - _lastY;
            }

            _lastX = x;
            _lastY = y;
        }

        public void MouseWheel(int x, int y, float offsetY, OrbitCamera camera, float cloudRadius)
        {
            camera.PickDepthWindow(x, y, 11);
            float zoomRatio = Math.Clamp((float)(camera.Hvs / cloudRadius), 0.1f, 5f);
            float step = Math.Clamp(zoomRatio * 0.25f, 0.1f, 0.5f);
            float factor = offsetY > 0 ? 1f + step : 1f / (1f + step);
            camera.Zoom(x, y, factor);
        }

        private void UpdatePivotAnimation(float dt, bool selectionVolumeActive)
        {
            bool orbiting = _leftDown || _orbitVelX != 0f || _orbitVelY != 0f;
            float target = orbiting && !selectionVolumeActive ? 1f : 0f;
            float rate = target > _pivotFade ? 8f : 5f;
            _pivotFade += (target - _pivotFade) * Math.Min(rate * dt, 1f);
            if (_pivotFlash > 0f)
                _pivotFlash = Math.Max(0f, _pivotFlash - dt * 2.5f);
        }

        private void UpdateInertia(OrbitCamera camera, int viewportWidth, int viewportHeight)
        {
            if (camera.IsTransitioning)
            {
                _orbitVelX = _orbitVelY = _panVelX = _panVelY = 0f;
                return;
            }

            if (!_leftDown && (_orbitVelX != 0f || _orbitVelY != 0f))
            {
                camera.Rotate(_orbitVelX, _orbitVelY);
                _orbitVelX *= 0.60f;
                _orbitVelY *= 0.60f;
                if (MathF.Abs(_orbitVelX) < 0.05f) _orbitVelX = 0f;
                if (MathF.Abs(_orbitVelY) < 0.05f) _orbitVelY = 0f;
            }

            if (!_rightDown && !_middleDown && (_panVelX != 0f || _panVelY != 0f))
            {
                int cx = viewportWidth / 2, cy = viewportHeight / 2;
                int dx = (int)MathF.Round(_panVelX), dy = (int)MathF.Round(_panVelY);
                if (dx != 0 || dy != 0)
                    camera.Pan(cx, cy, cx + dx, cy + dy);
                _panVelX *= 0.60f;
                _panVelY *= 0.60f;
                if (MathF.Abs(_panVelX) < 0.05f) _panVelX = 0f;
                if (MathF.Abs(_panVelY) < 0.05f) _panVelY = 0f;
            }
        }
    }
}
