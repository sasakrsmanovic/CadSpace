namespace CadSpace.Geometry;

/// <summary>Ear clipping for simple, planar XY polygons. Holes and self-intersections are explicitly rejected.</summary>
public static class Triangulation
{
    /// <summary>Triangulate a planar polygon in any orientation, retaining its input winding.</summary>
    public static int[] Polygon3D(IReadOnlyList<Vec3> polygon)
    {
        if (polygon.Count < 3) throw new ArgumentException("A face needs three vertices.");
        var origin = polygon[0]; var normal = Vec3.Zero;
        for (var i = 1; i + 1 < polygon.Count; i++) normal += (polygon[i] - origin).Cross(polygon[i + 1] - origin);
        if (normal.Length < 1e-14) throw new ArgumentException("A face has zero area.");
        var n = normal.Normalized;
        var extent = Math.Max(1, Bounds3.From(polygon).Size.Length);
        if (polygon.Any(p => !p.IsFinite || Math.Abs((p - origin).Dot(n)) > extent * 1e-8))
            throw new ArgumentException("Nonplanar faces need an explicit triangulation.");
        var x = (polygon[1] - origin).Normalized; var y = n.Cross(x).Normalized;
        return Polygon(polygon.Select(p => new Vec3((p - origin).Dot(x), (p - origin).Dot(y))).ToArray());
    }

    public static int[] Polygon(IReadOnlyList<Vec3> polygon)
    {
        if (polygon.Count < 3) throw new ArgumentException("A polygon needs at least three vertices.");
        var z = polygon[0].Z;
        if (polygon.Any(p => !p.IsFinite || Math.Abs(p.Z - z) > 1e-7)) throw new ArgumentException("The profile must be planar in XY.");
        for (var i = 0; i < polygon.Count; i++)
        {
            if (polygon[i].DistanceTo(polygon[(i + 1) % polygon.Count]) < 1e-9) throw new ArgumentException("The profile has duplicate adjacent vertices.");
            for (var j = i + 1; j < polygon.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == polygon.Count - 1)) continue;
                if (GeometryMath.IntersectLinesXY(polygon[i], polygon[(i + 1) % polygon.Count], polygon[j], polygon[(j + 1) % polygon.Count], out _))
                    throw new ArgumentException("Self-intersecting profiles are not supported.");
            }
        }
        var indices = Enumerable.Range(0, polygon.Count).ToList();
        if (GeometryMath.SignedArea(polygon) < 0) indices.Reverse();
        var result = new List<int>();
        while (indices.Count > 3)
        {
            var removed = false;
            for (var i = 0; i < indices.Count; i++)
            {
                var a = indices[(i + indices.Count - 1) % indices.Count];
                var b = indices[i]; var c = indices[(i + 1) % indices.Count];
                if (GeometryMath.Cross2(polygon[b] - polygon[a], polygon[c] - polygon[b]) <= 1e-12) continue;
                bool InTriangle(Vec3 p) => GeometryMath.Cross2(polygon[b] - polygon[a], p - polygon[a]) >= -1e-12 && GeometryMath.Cross2(polygon[c] - polygon[b], p - polygon[b]) >= -1e-12 && GeometryMath.Cross2(polygon[a] - polygon[c], p - polygon[c]) >= -1e-12;
                if (indices.Any(k => k != a && k != b && k != c && InTriangle(polygon[k]))) continue;
                result.AddRange([a, b, c]); indices.RemoveAt(i); removed = true; break;
            }
            if (!removed) throw new ArgumentException("The profile is degenerate or could not be triangulated.");
        }
        result.AddRange(indices);
        return result.ToArray();
    }
}
