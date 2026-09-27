namespace CadSpace.Geometry;

public readonly record struct Ray3(Vec3 Origin, Vec3 Direction)
{
    public Vec3 At(double distance) => Origin + Direction * distance;
    public bool IntersectPlane(Vec3 point, Vec3 normal, out Vec3 hit)
    {
        var divisor = normal.Dot(Direction); hit = default;
        if (Math.Abs(divisor) < 1e-12) return false;
        var distance = normal.Dot(point - Origin) / divisor;
        if (distance < 0) return false; hit = At(distance); return hit.IsFinite;
    }
    public bool IntersectTriangle(Vec3 a, Vec3 b, Vec3 c, out double distance)
    {
        // Model-space arithmetic remains double; the origin translation reduces large-coordinate cancellation.
        var e1 = b - a; var e2 = c - a; var p = Direction.Cross(e2); var det = e1.Dot(p); distance = 0;
        if (Math.Abs(det) <= 1e-12 * Math.Max(1e-12, e1.Length * e2.Length)) return false;
        var t = Origin - a; var u = t.Dot(p) / det; if (u < -1e-10 || u > 1 + 1e-10) return false;
        var q = t.Cross(e1); var v = Direction.Dot(q) / det; if (v < -1e-10 || u + v > 1 + 1e-10) return false;
        distance = e2.Dot(q) / det; return distance >= 0 && double.IsFinite(distance);
    }
}

public static class Coordinates3D
{
    /// <summary>Autodesk's documented arbitrary-axis OCS algorithm; the threshold is exactly 1/64.</summary>
    public static Transform3 ObjectCoordinateSystem(Vec3 normal)
    {
        var n = normal.Normalized;
        var x = (Math.Abs(n.X) < 1.0 / 64 && Math.Abs(n.Y) < 1.0 / 64 ? Vec3.UnitY.Cross(n) : Vec3.UnitZ.Cross(n)).Normalized;
        return new(x, n.Cross(x).Normalized, n, default);
    }
    public static Transform3 Inverse(Transform3 matrix)
    {
        var determinant = matrix.Determinant;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-18) throw new ArgumentException("The transform is singular.");
        var a = matrix.Y.Cross(matrix.Z) / determinant; var b = matrix.Z.Cross(matrix.X) / determinant; var c = matrix.X.Cross(matrix.Y) / determinant;
        var inverse = new Transform3(new(a.X, b.X, c.X), new(a.Y, b.Y, c.Y), new(a.Z, b.Z, c.Z), default);
        return inverse with { Origin = -inverse.Vector(matrix.Origin) };
    }
    public static Transform3 Frame(Vec3 origin, Vec3 xPoint, Vec3 yPoint)
    {
        var x = (xPoint - origin).Normalized; var z = x.Cross(yPoint - origin).Normalized;
        return new(x, z.Cross(x).Normalized, z, origin);
    }
}

/// <summary>Half-space retaining points with Normal.Dot(point) + Offset &lt;= 0.</summary>
public readonly record struct Plane3(Vec3 Normal, double Offset)
{
    public double SignedDistance(Vec3 point) => Normal.Dot(point) + Offset;
    public static Plane3 Through(Vec3 point, Vec3 normal) { var n = normal.Normalized; return new(n, -n.Dot(point)); }
}
