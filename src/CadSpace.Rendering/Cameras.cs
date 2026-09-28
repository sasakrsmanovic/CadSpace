using System.Numerics;
using CadSpace.Geometry;

namespace CadSpace.Rendering;

/// <summary>Double-precision orthographic camera. Rebase before converting coordinates to GPU floats.</summary>
public sealed class Camera2D
{
    public Vec3 Center { get; set; } = new(400, 250);
    public double PixelsPerUnit { get; set; } = 1;
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 700;
    public Vec3 WorldToScreen(Vec3 world) => new((world.X - Center.X) * PixelsPerUnit + Width / 2, Height / 2 - (world.Y - Center.Y) * PixelsPerUnit);
    public Vec3 ScreenToWorld(double x, double y) => new((x - Width / 2) / PixelsPerUnit + Center.X, (Height / 2 - y) / PixelsPerUnit + Center.Y);
    public Bounds3 VisibleBounds => Bounds3.From([ScreenToWorld(0, Height), ScreenToWorld(Width, 0)]);
    public void Pan(double screenDx, double screenDy) => Center += new Vec3(-screenDx / PixelsPerUnit, screenDy / PixelsPerUnit);
    public void Zoom(double factor, double screenX, double screenY)
    {
        var anchor = ScreenToWorld(screenX, screenY);
        PixelsPerUnit = Math.Clamp(PixelsPerUnit * factor, 1e-7, 1e7);
        Center += anchor - ScreenToWorld(screenX, screenY);
    }
    public void Fit(Bounds3 bounds)
    {
        if (bounds.IsEmpty) { Center = default; PixelsPerUnit = 1; return; }
        Center = bounds.Center;
        PixelsPerUnit = Math.Clamp(Math.Min(Math.Max(1, Width - 130) / Math.Max(bounds.Size.X, 50), Math.Max(1, Height - 100) / Math.Max(bounds.Size.Y, 50)), 1e-7, 1e7);
    }
}

public sealed class Camera3D
{
    public bool Orthographic { get; set; }
    public double Yaw { get; set; } = 45;
    public double Pitch { get; set; } = 30;
    public double Distance { get; set; } = 1000;
    public Vec3 Origin { get; set; }
    public Vec3 TargetOffset { get; set; }
    public void Fit(Bounds3 bounds)
    {
        Origin = bounds.Center; TargetOffset = default;
        Distance = Math.Max(10, bounds.Size.Length * 1.6);
    }
    public void Orbit(double dx, double dy) { Yaw += dx * 0.4; Pitch = Math.Clamp(Pitch + dy * 0.4, -89, 89); }
    public void Zoom(double factor) => Distance = Math.Clamp(Distance / factor, 1e-3, 1e12);
    public void Pan(double dx, double dy, double viewportHeight)
    {
        var yaw = GeometryMath.Radians(Yaw); var pitch = GeometryMath.Radians(Pitch);
        var right = new Vec3(-Math.Sin(yaw), Math.Cos(yaw));
        var up = new Vec3(-Math.Cos(yaw) * Math.Sin(pitch), -Math.Sin(yaw) * Math.Sin(pitch), Math.Cos(pitch));
        TargetOffset += (right * -dx + up * dy) * (Distance * 0.85 / Math.Max(1, viewportHeight));
    }
    public Vec3 Backward
    {
        get { var y = GeometryMath.Radians(Yaw); var p = GeometryMath.Radians(Pitch); return new(Math.Cos(y) * Math.Cos(p), Math.Sin(y) * Math.Cos(p), Math.Sin(p)); }
    }
    public Vec3 Right => new(-Math.Sin(GeometryMath.Radians(Yaw)), Math.Cos(GeometryMath.Radians(Yaw)));
    public Vec3 Up => Backward.Cross(Right);
    public Vec3 Target => Origin + TargetOffset;
    public Vec3 Eye => Target + Backward * Distance;
    public Ray3 Ray(double x, double y, double width, double height)
    {
        var half = Math.Tan(Math.PI / 8); var aspect = width / Math.Max(1, height);
        var offset = Right * ((2 * x / Math.Max(1, width) - 1) * half * aspect) + Up * ((1 - 2 * y / Math.Max(1, height)) * half);
        return Orthographic ? new(Eye + offset * Distance, -Backward) : new(Eye, (-Backward + offset).Normalized);
    }
    /// <summary>Screen X/Y and positive eye-space depth. Behind-eye points have nonpositive Z.</summary>
    public Vec3 Project(Vec3 point, double width, double height)
    {
        var delta = point - Eye; var depth = -delta.Dot(Backward);
        var half = Math.Tan(Math.PI / 8) * (Orthographic ? Distance : depth);
        if (Math.Abs(half) < 1e-15) return new(double.NaN, double.NaN, depth);
        return new(width / 2 + delta.Dot(Right) * height / (2 * half), height / 2 - delta.Dot(Up) * height / (2 * half), depth);
    }
    public void ZoomAt(double factor, double x, double y, double width, double height)
    {
        var before = Ray(x, y, width, height); var target = Target;
        var hit = before.IntersectPlane(target, Backward, out var anchor);
        Zoom(factor);
        if (hit && Ray(x, y, width, height).IntersectPlane(target, Backward, out var after)) TargetOffset += anchor - after;
    }
    public Matrix4x4 Matrix(double aspect)
    {
        var yaw = GeometryMath.Radians(Yaw); var pitch = GeometryMath.Radians(Pitch);
        var eye = TargetOffset + new Vec3(Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch)) * Distance;
        static Vector3 F(Vec3 p) => new((float)p.X, (float)p.Y, (float)p.Z);
        var view = Matrix4x4.CreateLookAt(F(eye), F(TargetOffset), F(Up));
        var near = (float)Math.Max(1e-5, Distance / 10000); var far = (float)(Distance * 100);
        var f = 1f / MathF.Tan(MathF.PI / 8);
        var projection = new Matrix4x4(f / (float)Math.Max(0.01, aspect), 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, 2 * far * near / (near - far), 0);
        if (Orthographic)
        {
            var half = (float)(Distance * Math.Tan(Math.PI / 8));
            projection = new Matrix4x4(1 / (half * (float)Math.Max(.01, aspect)), 0, 0, 0, 0, 1 / half, 0, 0, 0, 0, -2 / (far - near), 0, 0, 0, -(far + near) / (far - near), 1);
        }
        return view * projection;
    }
}
