using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Rendering;

public sealed record ViewCubeFace(string Name, Vec3 Normal, Vec3 U, Vec3 V, ImmutableArray<Vec3> Points);
public readonly record struct ViewOrientation(double Yaw, double Pitch);

/// <summary>Shared projection and 26-region face/edge/corner picking for a world-aligned navigation cube.</summary>
public static class ViewCubeGeometry
{
    public static readonly string[] Names = ["Top", "Bottom", "Front", "Back", "Left", "Right", "SW Isometric", "SE Isometric", "NE Isometric", "NW Isometric"];
    public static ViewOrientation Named(string name) => name switch {
        "Top" => new(-90, 90), "Bottom" => new(-90, -90), "Front" => new(-90, 0), "Back" => new(90, 0), "Left" => new(180, 0), "Right" => new(0, 0),
        "SW Isometric" => new(-135, 35.26438968), "SE Isometric" => new(-45, 35.26438968), "NE Isometric" => new(45, 35.26438968), "NW Isometric" => new(135, 35.26438968),
        _ => throw new ArgumentException("Unknown standard view.")
    };
    public static ImmutableArray<ViewCubeFace> Faces(double yaw, double pitch)
    {
        var y = GeometryMath.Radians(yaw); var p = GeometryMath.Radians(pitch);
        var back = new Vec3(Math.Cos(y) * Math.Cos(p), Math.Sin(y) * Math.Cos(p), Math.Sin(p));
        var right = new Vec3(-Math.Sin(y), Math.Cos(y)); var up = back.Cross(right);
        Vec3 Project(Vec3 v) => new(64 + v.Dot(right) * 24, 64 - v.Dot(up) * 24, v.Dot(back));
        var result = ImmutableArray.CreateBuilder<ViewCubeFace>();
        foreach (var (name, normal, u, v) in new[] { ("TOP", Vec3.UnitZ, Vec3.UnitX, Vec3.UnitY), ("BOTTOM", -Vec3.UnitZ, Vec3.UnitX, -Vec3.UnitY),
            ("FRONT", -Vec3.UnitY, Vec3.UnitX, Vec3.UnitZ), ("BACK", Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ),
            ("LEFT", -Vec3.UnitX, -Vec3.UnitY, Vec3.UnitZ), ("RIGHT", Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ) })
            if (normal.Dot(back) > 1e-8) result.Add(new(name, normal, u, v, [Project(normal - u - v), Project(normal + u - v), Project(normal + u + v), Project(normal - u + v)]));
        return result.ToImmutable();
    }
    public static ViewOrientation? Pick(ImmutableArray<ViewCubeFace> faces, double x, double y)
    {
        foreach (var face in faces)
        {
            var a = face.Points[0]; var u = face.Points[1] - a; var v = face.Points[3] - a; var delta = new Vec3(x, y) - a;
            var det = GeometryMath.Cross2(u, v); if (Math.Abs(det) < 1e-9) continue;
            var s = GeometryMath.Cross2(delta, v) / det; var t = GeometryMath.Cross2(u, delta) / det;
            if (s < 0 || s > 1 || t < 0 || t > 1) continue;
            var n = face.Normal + face.U * (s < .22 ? -1 : s > .78 ? 1 : 0) + face.V * (t < .22 ? -1 : t > .78 ? 1 : 0);
            return new(n.X == 0 && n.Y == 0 ? -90 : GeometryMath.Degrees(Math.Atan2(n.Y, n.X)), GeometryMath.Degrees(Math.Asin(n.Normalized.Z)));
        }
        return null;
    }
}
