using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

public sealed record ScenePath(Guid EntityId, string Layer, uint Color, ImmutableArray<Vec3> Points, bool Closed, bool Filled = false, double Weight = 0.25);
public sealed record SceneText(Guid EntityId, string Layer, uint Color, Vec3 Position, string Text, double Height, double Rotation);
public sealed record SceneTriangle(Guid EntityId, uint Color, Vec3 A, Vec3 B, Vec3 C);
public sealed record DrawingScene(ImmutableArray<ScenePath> Paths, ImmutableArray<SceneText> Texts, ImmutableArray<SceneTriangle> Triangles)
{
    public Bounds3 Bounds => Bounds3.From(Paths.SelectMany(p => p.Points).Concat(Texts.Select(t => t.Position)).Concat(Triangles.SelectMany(t => new[] { t.A, t.B, t.C })));
}

public static class EntityGeometry
{
    public static IEnumerable<Vec3> Anchors(Entity e) => e switch
    {
        LineEntity l => [l.Start, l.End], PointEntity p => [p.Position], CircleEntity c => [c.Center],
        ArcEntity a => [a.Center], EllipseEntity l => [l.Center, l.MajorAxis],
        PolylineEntity p => p.Vertices.Select(v => v.Position), TextEntity t => [t.Position],
        DimensionEntity d => [d.First, d.Second, d.Location], HatchEntity h => h.Boundary,
        MeshEntity m => m.Vertices, BlockReferenceEntity b => [b.Position], _ => []
    };
    public static uint AciColor(int index) => index switch
    {
        1 => 0xFFFF5A5A, 2 => 0xFFFFD966, 3 => 0xFF6BDB8B, 4 => 0xFF5EDBEB, 5 => 0xFF648DFF, 6 => 0xFFD984EC,
        7 => 0xFFE5E9EF, 8 => 0xFF808080, 9 => 0xFFC0C0C0, _ => 0xFFD8DFE8
    };
    public static ImmutableArray<Vec3> Curve(Vec3 center, double radius, double start, double sweep, int segments = 96)
    {
        var count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / 360 * segments), 2, 2048);
        return Enumerable.Range(0, count + 1).Select(i => GeometryMath.OnCircle(center, radius, start + sweep * i / count)).ToImmutableArray();
    }
    public static ImmutableArray<Vec3> PolylinePoints(PolylineEntity polyline)
    {
        var result = ImmutableArray.CreateBuilder<Vec3>();
        for (var i = 0; i < polyline.Vertices.Length; i++)
        {
            var a = polyline.Vertices[i]; result.Add(a.Position);
            if (i == polyline.Vertices.Length - 1 && !polyline.Closed) break;
            var b = polyline.Vertices[(i + 1) % polyline.Vertices.Length].Position;
            if (Math.Abs(a.Bulge) < 1e-12 || a.Position.DistanceTo(b) < 1e-12) continue;
            var chord = b - a.Position; var length = chord.Length;
            var normal = new Vec3(-chord.Y, chord.X) / length;
            var center = (a.Position + b) / 2 + normal * (length * (1 - a.Bulge * a.Bulge) / (4 * a.Bulge));
            var points = Curve(center, center.DistanceTo(a.Position), GeometryMath.Angle(a.Position - center), GeometryMath.Degrees(4 * Math.Atan(a.Bulge)));
            for (var j = 1; j < points.Length - 1; j++) result.Add(points[j]);
        }
        return result.ToImmutable();
    }
    public static DrawingScene BuildScene(Drawing drawing)
    {
        var paths = ImmutableArray.CreateBuilder<ScenePath>();
        var texts = ImmutableArray.CreateBuilder<SceneText>();
        var triangles = ImmutableArray.CreateBuilder<SceneTriangle>();
        void Add(Entity e, Transform3 transform, Guid root, string? inheritedLayer, uint? inheritedColor, int depth)
        {
            if (depth > 32) return;
            var layerName = e.Layer == "0" && inheritedLayer != null ? inheritedLayer : e.Layer;
            var layer = drawing.Layers.TryGetValue(layerName, out var l) ? l : drawing.Layers["0"];
            if (!layer.Visible) return;
            var color = e.TrueColor ?? (e.ColorIndex == 0 ? inheritedColor ?? layer.Color : e.ColorIndex == 256 ? layer.Color : AciColor(e.ColorIndex));
            var weight = e.LineWeight < 0 ? layer.LineWeight : e.LineWeight;
            void Path(IEnumerable<Vec3> points, bool closed = false, bool fill = false) => paths.Add(new(root, layerName, color, points.Select(transform.Point).ToImmutableArray(), closed, fill, weight));
            void Text(Vec3 p, string value, double height, double rotation = 0) => texts.Add(new(root, layerName, color, transform.Point(p), value, height * transform.Y.Length, rotation + GeometryMath.Angle(transform.X)));
            switch (e)
            {
                case LineEntity line: Path([line.Start, line.End]); break;
                case PointEntity point: Path([point.Position]); break;
                case CircleEntity circle: Path(Curve(circle.Center, circle.Radius, 0, 360), true); break;
                case ArcEntity arc: Path(Curve(arc.Center, arc.Radius, arc.StartAngle, GeometryMath.NormalizeAngle(arc.EndAngle - arc.StartAngle))); break;
                case PolylineEntity polyline: Path(PolylinePoints(polyline), polyline.Closed); break;
                case EllipseEntity ellipse:
                    var minor = new Vec3(-ellipse.MajorAxis.Y, ellipse.MajorAxis.X) * ellipse.Ratio;
                    var sweep = ellipse.EndParameter - ellipse.StartParameter;
                    if (sweep <= 0) sweep += 2 * Math.PI;
                    Path(Enumerable.Range(0, 129).Select(i => ellipse.Center + ellipse.MajorAxis * Math.Cos(ellipse.StartParameter + sweep * i / 128) + minor * Math.Sin(ellipse.StartParameter + sweep * i / 128)), Math.Abs(sweep - Math.PI * 2) < 1e-8); break;
                case TextEntity text: Text(text.Position, text.Text.Replace("\\P", "\n"), text.Height, text.Rotation); break;
                case DimensionEntity dim:
                    var vector = dim.Second - dim.First;
                    if (vector.Length < 1e-9) break;
                    var direction = vector.Normalized;
                    var normal = new Vec3(-direction.Y, direction.X);
                    var offset = (dim.Location - dim.First).Dot(normal);
                    var p1 = dim.First + normal * offset; var p2 = dim.Second + normal * offset;
                    Path([dim.First, p1 + normal * 4]); Path([dim.Second, p2 + normal * 4]); Path([p1, p2]);
                    Path([p1 + direction * 7 + normal * 2, p1, p1 + direction * 7 - normal * 2]);
                    Path([p2 - direction * 7 + normal * 2, p2, p2 - direction * 7 - normal * 2]);
                    Text((p1 + p2) / 2 + normal * 4, vector.Length.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), 10, GeometryMath.Angle(direction)); break;
                case HatchEntity hatch:
                    Path(hatch.Boundary, true, hatch.Solid);
                    if (!hatch.Solid && hatch.Spacing > 0 && hatch.Boundary.Length >= 3)
                    {
                        var rotate = Transform3.RotationZ(-hatch.Angle); var inverse = Transform3.RotationZ(hatch.Angle);
                        var polygon = hatch.Boundary.Select(rotate.Point).ToArray(); var bounds = Bounds3.From(polygon);
                        var spacing = Math.Max(hatch.Spacing, bounds.Size.Y / 2000);
                        for (var y = Math.Floor(bounds.Min.Y / spacing) * spacing; y <= bounds.Max.Y; y += spacing)
                        {
                            var intersections = new List<double>();
                            for (var i = 0; i < polygon.Length; i++)
                            {
                                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                                if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y)) intersections.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                            }
                            intersections.Sort();
                            for (var i = 0; i + 1 < intersections.Count; i += 2) Path([inverse.Point(new(intersections[i], y, polygon[0].Z)), inverse.Point(new(intersections[i + 1], y, polygon[0].Z))]);
                        }
                    }
                    break;
                case MeshEntity mesh:
                    for (var i = 0; i < mesh.Triangles.Length; i += 3)
                    {
                        var a = transform.Point(mesh.Vertices[mesh.Triangles[i]]); var b = transform.Point(mesh.Vertices[mesh.Triangles[i + 1]]); var c = transform.Point(mesh.Vertices[mesh.Triangles[i + 2]]);
                        triangles.Add(new(root, color, a, b, c));
                        paths.Add(new(root, layerName, color, [a, b, c], true, false, weight));
                    }
                    break;
                case BlockReferenceEntity insert when drawing.Blocks.TryGetValue(insert.Name, out var block):
                    var local = Transform3.Translation(-block.BasePoint).Then(Transform3.Scaling(insert.Scale)).Then(Transform3.RotationZ(insert.Rotation)).Then(Transform3.Translation(insert.Position)).Then(transform);
                    foreach (var child in block.Entities) Add(child, local, root, layerName, color, depth + 1);
                    break;
            }
        }
        foreach (var entity in drawing.Entities) Add(entity, Transform3.Identity, entity.Id, null, null, 0);
        return new(paths.ToImmutable(), texts.ToImmutable(), triangles.ToImmutable());
    }
    /// <summary>Similarity transforms preserve analytic curves. General affine transforms of curved entities are rejected.</summary>
    public static Entity Transform(Entity entity, Transform3 transform, bool copy = false)
    {
        var scale = transform.X.Length;
        var mirror = GeometryMath.Cross2(transform.X, transform.Y) < 0;
        if (entity is CircleEntity or ArcEntity or PolylineEntity or TextEntity or DimensionEntity or HatchEntity)
        {
            if (Math.Abs(transform.X.Dot(transform.Y)) > 1e-7 || Math.Abs(scale - transform.Y.Length) > 1e-7 || Math.Abs(transform.X.Z) + Math.Abs(transform.Y.Z) > 1e-7)
                throw new NotSupportedException("This curved/annotated entity requires an XY similarity transform.");
        }
        double Angle(double angle) => GeometryMath.Angle(transform.Vector(GeometryMath.OnCircle(default, 1, angle)));
        Entity result = entity switch
        {
            LineEntity l => l with { Start = transform.Point(l.Start), End = transform.Point(l.End) },
            PointEntity p => p with { Position = transform.Point(p.Position) },
            CircleEntity c => c with { Center = transform.Point(c.Center), Radius = c.Radius * scale },
            ArcEntity a => a with { Center = transform.Point(a.Center), Radius = a.Radius * scale, StartAngle = Angle(mirror ? a.EndAngle : a.StartAngle), EndAngle = Angle(mirror ? a.StartAngle : a.EndAngle) },
            PolylineEntity p => p with { Vertices = p.Vertices.Select(v => new PolyVertex(transform.Point(v.Position), mirror ? -v.Bulge : v.Bulge)).ToImmutableArray() },
            TextEntity t when !mirror => t with { Position = transform.Point(t.Position), Height = t.Height * scale, Rotation = Angle(t.Rotation) },
            DimensionEntity d => d with { First = transform.Point(d.First), Second = transform.Point(d.Second), Location = transform.Point(d.Location) },
            HatchEntity h => h with { Boundary = h.Boundary.Select(transform.Point).ToImmutableArray(), Spacing = h.Spacing * scale, Angle = Angle(h.Angle) },
            MeshEntity m => m with { Vertices = m.Vertices.Select(transform.Point).ToImmutableArray(), Triangles = transform.Determinant < 0 ? m.Triangles.Chunk(3).SelectMany(t => new[] { t[0], t[2], t[1] }).ToImmutableArray() : m.Triangles },
            BlockReferenceEntity b when !mirror => b with { Position = transform.Point(b.Position), Scale = b.Scale * scale, Rotation = Angle(b.Rotation) },
            _ => throw new NotSupportedException($"Transform is not supported for {entity.Kind}; no geometry was changed.")
        };
        return copy ? result with { Id = Guid.NewGuid(), Handle = "" } : result;
    }
}
