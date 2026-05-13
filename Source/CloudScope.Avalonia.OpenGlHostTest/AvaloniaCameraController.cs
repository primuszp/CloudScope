using OpenTK.Mathematics;

namespace CloudScope.Avalonia.OpenGlHostTest;

public sealed class AvaloniaCameraController
{
    private float _azimuth = 30f;
    private float _elevation = 25f;
    private float _distance = 100f;
    private float _radius = 50f;
    private Vector3 _target = Vector3.Zero;

    public float PointSize { get; private set; } = 2f;

    public void Fit(float radius)
    {
        _radius = Math.Max(radius, 1f);
        _distance = _radius * 2.8f;
        _target = Vector3.Zero;
        _azimuth = 30f;
        _elevation = 25f;
    }

    public void Orbit(float dx, float dy)
    {
        _azimuth -= dx * 0.35f;
        _elevation = Math.Clamp(_elevation - dy * 0.35f, -89f, 89f);
    }

    public void Pan(float dx, float dy, int viewportHeight)
    {
        float scale = Math.Max(_distance * 0.0018f, _radius / Math.Max(viewportHeight, 1));
        Vector3 right = GetRight();
        Vector3 up = GetUp();
        _target -= right * (dx * scale);
        _target += up * (dy * scale);
    }

    public void Zoom(float wheelDelta)
    {
        float factor = MathF.Pow(0.88f, wheelDelta);
        _distance = Math.Clamp(_distance * factor, _radius * 0.02f, _radius * 100f);
    }

    public void AdjustPointSize(float delta)
    {
        PointSize = Math.Clamp(PointSize + delta, 1f, 12f);
    }

    public float[] CreateMvp(int width, int height)
    {
        float aspect = width > 0 && height > 0 ? (float)width / height : 1f;
        Matrix4 view = Matrix4.LookAt(GetEye(), _target, Vector3.UnitZ);
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(45f),
            aspect,
            Math.Max(_radius * 0.001f, 0.01f),
            Math.Max(_radius * 20f, _distance + _radius * 4f));

        Matrix4 mvp = view * projection;
        return
        [
            mvp.M11, mvp.M12, mvp.M13, mvp.M14,
            mvp.M21, mvp.M22, mvp.M23, mvp.M24,
            mvp.M31, mvp.M32, mvp.M33, mvp.M34,
            mvp.M41, mvp.M42, mvp.M43, mvp.M44
        ];
    }

    private Vector3 GetEye()
    {
        float az = MathHelper.DegreesToRadians(_azimuth);
        float el = MathHelper.DegreesToRadians(_elevation);
        float cosEl = MathF.Cos(el);
        Vector3 offset = new(
            MathF.Cos(az) * cosEl,
            MathF.Sin(az) * cosEl,
            MathF.Sin(el));
        return _target + offset * _distance;
    }

    private Vector3 GetRight()
    {
        Vector3 forward = (_target - GetEye()).Normalized();
        return Vector3.Cross(forward, Vector3.UnitZ).Normalized();
    }

    private Vector3 GetUp()
    {
        Vector3 forward = (_target - GetEye()).Normalized();
        Vector3 right = GetRight();
        return Vector3.Cross(right, forward).Normalized();
    }
}
