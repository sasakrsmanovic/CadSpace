using System.Globalization;

namespace CadSpace.Geometry;

/// <summary>Double precision model-space vector. Conversion to float belongs only at the GPU boundary.</summary>
public readonly record struct Vec3(double X, double Y, double Z = 0)
{
    public static Vec3 Zero => default;
    public static Vec3 UnitX => new(1, 0, 0);
    public static Vec3 UnitY => new(0, 1, 0);
    public static Vec3 UnitZ => new(0, 0, 1);
    public double Length => Math.Sqrt(Dot(this));
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public Vec3 Normalized => Length > 1e-15 ? this / Length : throw new ArgumentException("A zero vector has no direction.");
    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double DistanceTo(Vec3 b) => (this - b).Length;
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public override string ToString() => FormattableString.Invariant($"{X:0.###},{Y:0.###},{Z:0.###}");
}

/// <summary>Column-basis affine transform; A.Then(B) applies A, then B.</summary>
public readonly record struct Transform3(Vec3 X, Vec3 Y, Vec3 Z, Vec3 Origin)
{
    public static Transform3 Identity => new(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, Vec3.Zero);
    public Vec3 Vector(Vec3 p) => X * p.X + Y * p.Y + Z * p.Z;
    public Vec3 Point(Vec3 p) => Vector(p) + Origin;
    public double Determinant => X.Dot(Y.Cross(Z));
    public Transform3 Then(Transform3 next) => new(next.Vector(X), next.Vector(Y), next.Vector(Z), next.Point(Origin));
    public static Transform3 Translation(Vec3 offset) => Identity with { Origin = offset };
    public static Transform3 Scaling(Vec3 factors, Vec3 center = default) => new Transform3(
        Vec3.UnitX * factors.X, Vec3.UnitY * factors.Y, Vec3.UnitZ * factors.Z, Vec3.Zero).About(center);
    public Transform3 About(Vec3 center) => this with { Origin = center - Vector(center) + Origin };
    public static Transform3 RotationZ(double degrees, Vec3 center = default)
    {
        var r = degrees * Math.PI / 180;
        return new Transform3(new(Math.Cos(r), Math.Sin(r)), new(-Math.Sin(r), Math.Cos(r)), Vec3.UnitZ, default).About(center);
    }
    public static Transform3 RotationAxis(Vec3 axis, double degrees, Vec3 center = default)
    {
        var n = axis.Normalized;
        var a = degrees * Math.PI / 180;
        Vec3 Rotate(Vec3 p) => p * Math.Cos(a) + n.Cross(p) * Math.Sin(a) + n * (n.Dot(p) * (1 - Math.Cos(a)));
        return new Transform3(Rotate(Vec3.UnitX), Rotate(Vec3.UnitY), Rotate(Vec3.UnitZ), default).About(center);
    }
    public static Transform3 MirrorXY(Vec3 a, Vec3 b)
    {
        var d = new Vec3(b.X - a.X, b.Y - a.Y).Normalized;
        return new Transform3(new(2 * d.X * d.X - 1, 2 * d.X * d.Y), new(2 * d.X * d.Y, 2 * d.Y * d.Y - 1), Vec3.UnitZ, default).About(a);
    }
}

public readonly record struct Bounds3(Vec3 Min, Vec3 Max)
{
    public static Bounds3 Empty => new(new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));
    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vec3 Center => IsEmpty ? default : (Min + Max) / 2;
    public Vec3 Size => IsEmpty ? default : Max - Min;
    public Bounds3 Include(Vec3 p) => new(new(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)), new(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));
    public Bounds3 Union(Bounds3 b) => b.IsEmpty ? this : Include(b.Min).Include(b.Max);
    public bool ContainsXY(Vec3 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
    public bool IntersectsXY(Bounds3 b) => Max.X >= b.Min.X && Min.X <= b.Max.X && Max.Y >= b.Min.Y && Min.Y <= b.Max.Y;
    public static Bounds3 From(IEnumerable<Vec3> points) => points.Aggregate(Empty, (b, p) => b.Include(p));
}

public static class GeometryMath
{
    public const double Epsilon = 1e-9;
    public static double Degrees(double radians) => radians * 180 / Math.PI;
    public static double Radians(double degrees) => degrees * Math.PI / 180;
    public static double NormalizeAngle(double degrees) => (degrees % 360 + 360) % 360;
    public static double Angle(Vec3 vector) => Degrees(Math.Atan2(vector.Y, vector.X));
    public static double Cross2(Vec3 a, Vec3 b) => a.X * b.Y - a.Y * b.X;
    public static Vec3 OnCircle(Vec3 center, double radius, double degrees) => center + new Vec3(Math.Cos(Radians(degrees)), Math.Sin(Radians(degrees))) * radius;
    public static Vec3 NearestOnSegment(Vec3 p, Vec3 a, Vec3 b)
    {
        var d = b - a;
        var length2 = d.Dot(d);
        return length2 < Epsilon * Epsilon ? a : a + d * Math.Clamp((p - a).Dot(d) / length2, 0, 1);
    }
    public static bool IntersectLinesXY(Vec3 a, Vec3 b, Vec3 c, Vec3 d, out Vec3 intersection, bool segments = true)
    {
        var u = b - a; var v = d - c; var denominator = Cross2(u, v);
        intersection = default;
        if (Math.Abs(denominator) <= Epsilon * Math.Max(1, u.Length * v.Length)) return false;
        var t = Cross2(c - a, v) / denominator;
        var s = Cross2(c - a, u) / denominator;
        if (segments && (t < -Epsilon || t > 1 + Epsilon || s < -Epsilon || s > 1 + Epsilon)) return false;
        intersection = a + u * t;
        return intersection.IsFinite;
    }
    public static (Vec3 Center, double Radius) CircleThrough(Vec3 a, Vec3 b, Vec3 c)
    {
        var u = b - a; var v = c - a;
        var divisor = 2 * Cross2(u, v);
        if (Math.Abs(divisor) < Epsilon) throw new ArgumentException("The three points are collinear.");
        var center = a + new Vec3((u.Dot(u) * v.Y - v.Dot(v) * u.Y) / divisor, (v.Dot(v) * u.X - u.Dot(u) * v.X) / divisor);
        return (center, center.DistanceTo(a));
    }
    public static double SignedArea(IReadOnlyList<Vec3> polygon)
    {
        double area = 0;
        for (var i = 0; i < polygon.Count; i++) area += Cross2(polygon[i], polygon[(i + 1) % polygon.Count]);
        return area / 2;
    }
    public static bool PointInPolygon(Vec3 point, IReadOnlyList<Vec3> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    /// <summary>Parse invariant absolute, @relative, and @distance&lt;angle coordinates.</summary>
    public static bool TryParsePoint(string text, Vec3 origin, out Vec3 point)
    {
        point = default;
        text = text.Trim();
        var relative = text.StartsWith('@');
        if (relative) text = text[1..];
        var polar = text.Split('<');
        if (polar.Length == 2 && Number(polar[0], out var distance) && Number(polar[1], out var angle))
        {
            point = OnCircle(relative ? origin : default, distance, angle);
            return point.IsFinite;
        }
        var parts = text.Split(',');
        if (parts.Length is < 2 or > 3 || !Number(parts[0], out var x) || !Number(parts[1], out var y)) return false;
        double z = 0;
        if (parts.Length == 3 && !Number(parts[2], out z)) return false;
        point = new Vec3(x, y, z) + (relative ? origin : default);
        return point.IsFinite;
    }
    public static bool Number(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
