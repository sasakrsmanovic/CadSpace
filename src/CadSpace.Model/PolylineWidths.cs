using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Explicit fill strips for native polyline widths. Curves are sampled; joins are bounded bevels.</summary>
public static class PolylineWidths
{
    public sealed record Strip(ImmutableArray<Vec3> Vertices, ImmutableArray<int> Triangles, ImmutableArray<Vec3> Outline);
    public static void Validate(PolylineEntity polyline)
    {
        static bool Width(double value) => double.IsFinite(value) && value >= 0 && value <= 1e12;
        if (!Width(polyline.ConstantWidth) || polyline.Vertices.Any(v => !Width(v.StartWidth) || !Width(v.EndWidth)))
            throw new ArgumentException("Polyline widths must be finite, nonnegative and no larger than 1e12.");
        if (polyline.HasWidth && polyline.Vertices.Length > 0 && polyline.Vertices.Any(v => Math.Abs(v.Position.Z - polyline.Vertices[0].Position.Z) > 1e-8))
            throw new ArgumentException("Wide polylines require a planar XY centerline in their local coordinate system.");
    }

    /// <summary>Build O(vertices + samples) strips, with no global polygon-union operation.</summary>
    public static IEnumerable<Strip> Build(PolylineEntity polyline)
    {
        Validate(polyline);
        if (polyline.Vertices.Length < 2) yield break;
        long samples = 0;
        var count = polyline.Vertices.Length - (polyline.Closed ? 0 : 1);
        (Vec3 L, Vec3 R, Vec3 Center)? previous = null, first = null;
        for (var i = 0; i < count; i++)
        {
            var vertex = polyline.Vertices[i]; var a = vertex.Position;
            var b = polyline.Vertices[(i + 1) % polyline.Vertices.Length].Position;
            var chord = b - a; var length = chord.Length;
            if (length < 1e-12) continue;
            var startWidth = polyline.ConstantWidth > 0 ? polyline.ConstantWidth : vertex.StartWidth;
            var endWidth = polyline.ConstantWidth > 0 ? polyline.ConstantWidth : vertex.EndWidth;
            if (startWidth == 0 && endWidth == 0)
            {
                var line = EntityGeometry.PolylinePoints(new([vertex, new(b)]));
                samples += line.Length;
                if (samples > 500000) throw new ArgumentException("Wide polyline exceeds its 500,000-sample budget.");
                yield return new(line, [], line); previous = null; continue;
            }
            var sweep = 4 * Math.Atan(vertex.Bulge);
            var curved = Math.Abs(vertex.Bulge) >= 1e-12;
            var steps = curved ? Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * 256), 2, 256) : 1;
            samples += steps + 1;
            if (samples > 500000) throw new ArgumentException("Wide polyline exceeds its 500,000-sample budget.");
            var tangent = chord / length;
            var center = curved ? a + chord / 2 + new Vec3(-tangent.Y, tangent.X) * (length * (1 - vertex.Bulge * vertex.Bulge) / (4 * vertex.Bulge)) : default;
            var radius = curved ? center.DistanceTo(a) : 0;
            var angle = curved ? Math.Atan2(a.Y - center.Y, a.X - center.X) : 0;
            var vertices = new Vec3[(steps + 1) * 2];
            for (var j = 0; j <= steps; j++)
            {
                var t = (double)j / steps; var theta = angle + sweep * t;
                var point = curved ? center + new Vec3(Math.Cos(theta), Math.Sin(theta)) * radius : a + chord * t;
                // Retain exact endpoints instead of trigonometric endpoint drift.
                if (j == 0) point = a; else if (j == steps) point = b;
                var direction = curved ? new Vec3(-Math.Sin(theta), Math.Cos(theta)) * Math.Sign(sweep) : tangent;
                var half = (startWidth * (1 - t) + endWidth * t) / 2;
                var offset = new Vec3(-direction.Y, direction.X) * half;
                vertices[2 * j] = point + offset; vertices[2 * j + 1] = point - offset;
            }
            var begin = (L: vertices[0], R: vertices[1], Center: a);
            if (i == 0) first = begin;
            if (previous is { } end && end.Center.DistanceTo(a) < 1e-8)
                foreach (var join in Bevel(end.L, end.R, begin.L, begin.R, a)) yield return join;
            previous = (vertices[^2], vertices[^1], b);
            var triangles = ImmutableArray.CreateBuilder<int>(steps * 6);
            for (var j = 0; j < steps; j++)
            {
                AddTriangle(vertices, triangles, 2 * j, 2 * j + 1, 2 * j + 3);
                AddTriangle(vertices, triangles, 2 * j, 2 * j + 3, 2 * j + 2);
            }
            var outline = ImmutableArray.CreateBuilder<Vec3>((steps + 1) * 2);
            for (var j = 0; j <= steps; j++) outline.Add(vertices[2 * j]);
            for (var j = steps; j >= 0; j--) outline.Add(vertices[2 * j + 1]);
            yield return new(vertices.ToImmutableArray(), triangles.ToImmutable(), outline.ToImmutable());
        }
        if (polyline.Closed && previous is { } last && first is { } beginning && last.Center.DistanceTo(beginning.Center) < 1e-8)
            foreach (var join in Bevel(last.L, last.R, beginning.L, beginning.R, beginning.Center)) yield return join;
    }
    private static IEnumerable<Strip> Bevel(Vec3 oldLeft, Vec3 oldRight, Vec3 newLeft, Vec3 newRight, Vec3 center)
    {
        foreach (var (a, b) in new[] { (oldLeft, newLeft), (oldRight, newRight) })
        {
            var points = new[] { center, a, b }; var indices = ImmutableArray.CreateBuilder<int>(3);
            AddTriangle(points, indices, 0, 1, 2);
            if (indices.Count > 0) yield return new(points.ToImmutableArray(), indices.ToImmutable(), points.ToImmutableArray());
        }
    }
    private static void AddTriangle(Vec3[] vertices, ImmutableArray<int>.Builder triangles, int a, int b, int c)
    {
        var area = GeometryMath.Cross2(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Math.Abs(area) < 1e-18) return;
        if (area < 0) (b, c) = (c, b);
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
    }
}
