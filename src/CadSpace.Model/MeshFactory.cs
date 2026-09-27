using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Explicit triangle-mesh modeling, not an ACIS/B-rep solid kernel.</summary>
public static class MeshFactory
{
    public static MeshEntity Box(Vec3 first, Vec3 opposite)
    {
        var min = new Vec3(Math.Min(first.X, opposite.X), Math.Min(first.Y, opposite.Y), Math.Min(first.Z, opposite.Z));
        var max = new Vec3(Math.Max(first.X, opposite.X), Math.Max(first.Y, opposite.Y), Math.Max(first.Z, opposite.Z));
        if (Math.Min(max.X - min.X, Math.Min(max.Y - min.Y, max.Z - min.Z)) <= 1e-9) throw new ArgumentException("Box dimensions must be nonzero.");
        return Extrude([min, new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z)], max.Z - min.Z) with { Operation = "Box" };
    }
    public static MeshEntity Extrude(IReadOnlyList<Vec3> profile, double height)
    {
        if (!double.IsFinite(height) || Math.Abs(height) < 1e-9) throw new ArgumentException("Height must be finite and nonzero.");
        var points = profile.ToArray();
        if (GeometryMath.SignedArea(points) < 0) Array.Reverse(points);
        var cap = Triangulation.Polygon(points); var n = points.Length;
        var vertices = points.Concat(points.Select(p => p + Vec3.UnitZ * height)).ToImmutableArray();
        var triangles = new List<int>();
        for (var i = 0; i < cap.Length; i += 3) triangles.AddRange([cap[i + 2], cap[i + 1], cap[i], cap[i] + n, cap[i + 1] + n, cap[i + 2] + n]);
        for (var i = 0; i < n; i++) { var j = (i + 1) % n; triangles.AddRange([i, j, j + n, i, j + n, i + n]); }
        if (height < 0) for (var i = 0; i < triangles.Count; i += 3) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        return new(vertices, triangles.ToImmutableArray(), "Extrude");
    }
    public static MeshEntity Cylinder(Vec3 center, double radius, double height, int segments = 64)
    {
        Positive(radius); Segments(segments);
        return Extrude(Enumerable.Range(0, segments).Select(i => GeometryMath.OnCircle(center, radius, 360.0 * i / segments)).ToArray(), height) with { Operation = "Cylinder" };
    }
    public static MeshEntity Cone(Vec3 center, double radius, double height, int segments = 64)
    {
        Positive(radius); Positive(height); Segments(segments);
        var vertices = Enumerable.Range(0, segments).Select(i => GeometryMath.OnCircle(center, radius, 360.0 * i / segments)).Append(center).Append(center + Vec3.UnitZ * height).ToImmutableArray();
        var triangles = new List<int>();
        for (var i = 0; i < segments; i++) { var j = (i + 1) % segments; triangles.AddRange([segments, j, i, i, j, segments + 1]); }
        return new(vertices, triangles.ToImmutableArray(), "Cone");
    }
    public static MeshEntity Sphere(Vec3 center, double radius, int segments = 48, int rings = 24)
    {
        Positive(radius); Segments(segments); Segments(rings);
        var v = new List<Vec3> { center + Vec3.UnitZ * radius };
        for (var ring = 1; ring < rings; ring++)
        {
            var phi = Math.PI * ring / rings;
            for (var j = 0; j < segments; j++) { var a = 2 * Math.PI * j / segments; v.Add(center + new Vec3(Math.Sin(phi) * Math.Cos(a), Math.Sin(phi) * Math.Sin(a), Math.Cos(phi)) * radius); }
        }
        var south = v.Count; v.Add(center - Vec3.UnitZ * radius);
        var t = new List<int>();
        for (var j = 0; j < segments; j++)
        {
            var k = (j + 1) % segments; t.AddRange([0, 1 + j, 1 + k]);
            for (var r = 0; r < rings - 2; r++) { var a = 1 + r * segments + j; var b = 1 + r * segments + k; t.AddRange([a, a + segments, b + segments, a, b + segments, b]); }
            var last = 1 + (rings - 2) * segments; t.AddRange([last + j, south, last + k]);
        }
        return new(v.ToImmutableArray(), t.ToImmutableArray(), "Sphere");
    }
    public static MeshEntity Revolve(IReadOnlyList<Vec3> profile, Vec3 axisStart, Vec3 axisEnd, double degrees = 360, int segments = 64)
    {
        Segments(segments);
        if (profile.Count < 2 || !double.IsFinite(degrees) || Math.Abs(degrees) < 1e-8 || Math.Abs(degrees) > 360) throw new ArgumentException("Invalid revolve profile or angle.");
        var axis = (axisEnd - axisStart).Normalized;
        var vertices = new List<Vec3>();
        for (var i = 0; i <= segments; i++) vertices.AddRange(profile.Select(Transform3.RotationAxis(axis, degrees * i / segments, axisStart).Point));
        var indices = new List<int>(); var n = profile.Count;
        for (var ring = 0; ring < segments; ring++) for (var j = 0; j + 1 < n; j++) { var a = ring * n + j; indices.AddRange([a, a + n, a + n + 1, a, a + n + 1, a + 1]); }
        return new(vertices.ToImmutableArray(), indices.ToImmutableArray(), "Revolved surface");
    }
    public static double SignedVolume(MeshEntity mesh)
    {
        // Translate to reduce cancellation for large georeferenced coordinates.
        if (mesh.Vertices.IsEmpty) return 0;
        var origin = mesh.Vertices[0]; double volume = 0;
        for (var i = 0; i < mesh.Triangles.Length; i += 3) volume += (mesh.Vertices[mesh.Triangles[i]] - origin).Dot((mesh.Vertices[mesh.Triangles[i + 1]] - origin).Cross(mesh.Vertices[mesh.Triangles[i + 2]] - origin)) / 6;
        return volume;
    }
    private static void Positive(double value) { if (!double.IsFinite(value) || value <= 0) throw new ArgumentException("Value must be finite and positive."); }
    private static void Segments(int value) { if (value is < 3 or > 512) throw new ArgumentOutOfRangeException(nameof(value), "Resolution must be between 3 and 512."); }
}
