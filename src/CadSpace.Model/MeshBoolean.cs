using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

public enum MeshBooleanOperation { Union, Subtract, Intersect }

/// <summary>Bounded, double-precision BSP Boolean operations on closed, consistently oriented triangle meshes. Not an analytic B-rep kernel.</summary>
public static class MeshBoolean
{
    public static MeshEntity Apply(MeshEntity first, MeshEntity second, MeshBooleanOperation operation, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (first.Triangles.Length + second.Triangles.Length > 48000) throw new ArgumentException("Mesh Booleans are limited to 16,000 input triangles.");
        var bounds = Bounds3.From(first.Vertices.Concat(second.Vertices)); var origin = bounds.Center;
        var tolerance = Math.Max(1e-10, bounds.Size.Length * 1e-9);
        var context = new Context(tolerance, cancellationToken);
        var a = new Node(); var b = new Node();
        a.Build(Polygons(first, origin, context), context); b.Build(Polygons(second, origin, context), context);
        if (operation == MeshBooleanOperation.Union)
        {
            a.ClipTo(b, context); b.ClipTo(a, context); b.Invert(); b.ClipTo(a, context); b.Invert(); a.Build(b.All(), context);
        }
        else if (operation == MeshBooleanOperation.Subtract)
        {
            a.Invert(); a.ClipTo(b, context); b.ClipTo(a, context); b.Invert(); b.ClipTo(a, context); b.Invert(); a.Build(b.All(), context); a.Invert();
        }
        else
        {
            a.Invert(); b.ClipTo(a, context); b.Invert(); a.ClipTo(b, context); b.ClipTo(a, context); a.Build(b.All(), context); a.Invert();
        }
        var result = Stitch(a.All(), origin, context, operation.ToString());
        if (!result.Triangles.IsEmpty) ValidateClosed(result, tolerance);
        return result;
    }
    public static void ValidateClosed(MeshEntity mesh, double tolerance = 1e-8)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (mesh.Vertices.IsEmpty || mesh.Triangles.Length < 12 || mesh.Triangles.Length % 3 != 0 || mesh.Vertices.Any(v => !v.IsFinite)) throw new ArgumentException("A Boolean operand must be a finite closed triangle mesh.");
        var weld = new Weld(tolerance); var origin = mesh.Vertices[0]; var ids = mesh.Vertices.Select(p => weld.Add(p - origin)).ToArray();
        var edges = new Dictionary<(int, int), (int Count, int Orientation)>();
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
        {
            var t = mesh.Triangles.AsSpan(i, 3); if (t[0] < 0 || t[1] < 0 || t[2] < 0 || t[0] >= ids.Length || t[1] >= ids.Length || t[2] >= ids.Length) throw new ArgumentException("Invalid mesh triangle index.");
            var a = ids[t[0]]; var b = ids[t[1]]; var c = ids[t[2]];
            if (a == b || b == c || c == a || (weld.Points[b] - weld.Points[a]).Cross(weld.Points[c] - weld.Points[a]).Length <= tolerance * tolerance) throw new ArgumentException("The mesh has degenerate triangles.");
            Add(a, b); Add(b, c); Add(c, a);
        }
        void Add(int a, int b) { var key = (Math.Min(a, b), Math.Max(a, b)); var old = edges.GetValueOrDefault(key); edges[key] = (old.Count + 1, old.Orientation + (a < b ? 1 : -1)); }
        if (edges.Values.Any(e => e.Count != 2 || e.Orientation != 0)) throw new ArgumentException("Mesh Booleans require closed, two-manifold edges with consistent orientation.");
        if (Math.Abs(MeshFactory.SignedVolume(mesh)) <= tolerance * tolerance * tolerance) throw new ArgumentException("The mesh has no enclosed volume.");
    }
    private static List<Polygon> Polygons(MeshEntity mesh, Vec3 origin, Context context)
    {
        ValidateClosed(mesh, context.Epsilon); var invert = MeshFactory.SignedVolume(mesh) < 0; var result = new List<Polygon>();
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Triangles[i]] - origin; var b = mesh.Vertices[mesh.Triangles[i + 1]] - origin; var c = mesh.Vertices[mesh.Triangles[i + 2]] - origin;
            result.Add(new(invert ? [c, b, a] : [a, b, c]));
        }
        return result;
    }
    private sealed class Context(double epsilon, CancellationToken cancellation)
    {
        private long _work;
        public double Epsilon { get; } = epsilon;
        public void Tick(long amount = 1) { _work += amount; if (_work > 20000000) throw new InvalidOperationException("The mesh Boolean exceeded its 20-million-step work budget."); cancellation.ThrowIfCancellationRequested(); }
    }
    private sealed class Polygon(List<Vec3> points)
    {
        public List<Vec3> Points { get; } = points;
        public Vec3 Normal { get; private set; } = AreaVector(points).Normalized;
        public static Vec3 AreaVector(List<Vec3> p) { var n = Vec3.Zero; for (var i = 1; i + 1 < p.Count; i++) n += (p[i] - p[0]).Cross(p[i + 1] - p[0]); return n; }
        public void Invert() { Points.Reverse(); Normal = -Normal; }
    }
    private sealed class Node
    {
        private Plane3? _plane; private List<Polygon> _polygons = new(); private Node? _front, _back;
        private IEnumerable<Node> Nodes()
        {
            var stack = new Stack<Node>(); stack.Push(this);
            while (stack.TryPop(out var node)) { yield return node; if (node._front != null) stack.Push(node._front); if (node._back != null) stack.Push(node._back); }
        }
        public List<Polygon> All() => Nodes().SelectMany(n => n._polygons).ToList();
        public void Invert() { foreach (var node in Nodes()) { foreach (var p in node._polygons) p.Invert(); if (node._plane is Plane3 plane) node._plane = new(-plane.Normal, -plane.Offset); (node._front, node._back) = (node._back, node._front); } }
        public void Build(List<Polygon> polygons, Context context)
        {
            var stack = new Stack<(Node Node, List<Polygon> Polygons)>(); stack.Push((this, polygons));
            while (stack.TryPop(out var item))
            {
                var (node, input) = item; if (input.Count == 0) continue; context.Tick();
                node._plane ??= Plane3.Through(input[0].Points[0], input[0].Normal);
                var front = new List<Polygon>(); var back = new List<Polygon>();
                foreach (var polygon in input) Split(node._plane.Value, polygon, node._polygons, node._polygons, front, back, context);
                if (front.Count > 0) stack.Push((node._front ??= new(), front)); if (back.Count > 0) stack.Push((node._back ??= new(), back));
            }
        }
        private List<Polygon> Clip(List<Polygon> polygons, Context context)
        {
            var result = new List<Polygon>(); var stack = new Stack<(Node Node, List<Polygon> Polygons)>(); stack.Push((this, polygons));
            while (stack.TryPop(out var item))
            {
                var (node, input) = item; if (node._plane is not Plane3 plane) { result.AddRange(input); continue; }
                var front = new List<Polygon>(); var back = new List<Polygon>();
                foreach (var polygon in input) Split(plane, polygon, front, back, front, back, context);
                if (node._front != null) stack.Push((node._front, front)); else result.AddRange(front);
                if (node._back != null) stack.Push((node._back, back));
            }
            return result;
        }
        public void ClipTo(Node other, Context context) { foreach (var node in Nodes()) node._polygons = other.Clip(node._polygons, context); }
        private static void Split(Plane3 plane, Polygon polygon, List<Polygon> same, List<Polygon> opposite, List<Polygon> front, List<Polygon> back, Context context)
        {
            context.Tick(); var types = polygon.Points.Select(p => plane.SignedDistance(p) > context.Epsilon ? 1 : plane.SignedDistance(p) < -context.Epsilon ? 2 : 0).ToArray();
            var type = types.Aggregate(0, (a, b) => a | b);
            if (type == 0) { (plane.Normal.Dot(polygon.Normal) >= 0 ? same : opposite).Add(polygon); return; }
            if (type == 1) { front.Add(polygon); return; } if (type == 2) { back.Add(polygon); return; }
            var a = new List<Vec3>(); var b = new List<Vec3>();
            for (var i = 0; i < polygon.Points.Count; i++)
            {
                var j = (i + 1) % polygon.Points.Count; var p = polygon.Points[i]; var q = polygon.Points[j];
                if (types[i] != 2) a.Add(p); if (types[i] != 1) b.Add(p);
                if ((types[i] | types[j]) == 3) { var t = -plane.SignedDistance(p) / plane.Normal.Dot(q - p); var hit = Vec3.Lerp(p, q, Math.Clamp(t, 0, 1)); a.Add(hit); b.Add(hit); }
            }
            Add(a, front); Add(b, back);
            void Add(List<Vec3> points, List<Polygon> output)
            {
                for (var i = points.Count - 1; i >= 0 && points.Count > 2; i--) if (points[i].DistanceTo(points[(i + 1) % points.Count]) <= context.Epsilon) points.RemoveAt(i);
                if (points.Count >= 3 && Polygon.AreaVector(points).Length > context.Epsilon * context.Epsilon) output.Add(new(points));
            }
        }
    }
    private sealed class Weld(double tolerance)
    {
        private readonly Dictionary<(long, long, long), List<int>> _cells = new();
        public List<Vec3> Points { get; } = new();
        public int Add(Vec3 point)
        {
            var key = ((long)Math.Floor(point.X / tolerance), (long)Math.Floor(point.Y / tolerance), (long)Math.Floor(point.Z / tolerance));
            for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++) for (var z = -1; z <= 1; z++)
                if (_cells.TryGetValue((key.Item1 + x, key.Item2 + y, key.Item3 + z), out var ids)) foreach (var id in ids) if (Points[id].DistanceTo(point) <= tolerance) return id;
            if (!_cells.TryGetValue(key, out var bucket)) _cells[key] = bucket = new(); var index = Points.Count; Points.Add(point); bucket.Add(index); return index;
        }
    }
    private static MeshEntity Stitch(List<Polygon> polygons, Vec3 origin, Context context, string operation)
    {
        var weld = new Weld(context.Epsilon); var faces = polygons.Select(p => p.Points.Select(weld.Add).ToList()).ToArray(); var vertices = weld.Points.ToArray();
        if (vertices.Length > 100000 || faces.Length > 100000) throw new InvalidOperationException("Mesh Boolean output exceeded 100,000 vertices/faces.");
        var triangles = new List<int>();
        foreach (var face in faces)
        {
            var split = new List<int>();
            for (var i = 0; i < face.Count; i++)
            {
                var a = vertices[face[i]]; var b = vertices[face[(i + 1) % face.Count]]; var d = b - a; var length2 = d.Dot(d); if (length2 <= context.Epsilon * context.Epsilon) continue;
                split.Add(face[i]); var points = new List<(int Id, double T)>();
                for (var k = 0; k < vertices.Length; k++)
                {
                    context.Tick(); var t = (vertices[k] - a).Dot(d) / length2;
                    if (t > 1e-9 && t < 1 - 1e-9 && vertices[k].DistanceTo(a + d * t) <= context.Epsilon) points.Add((k, t));
                }
                split.AddRange(points.OrderBy(p => p.T).Select(p => p.Id));
            }
            if (split.Count < 3) continue;
            var center = split.Select(i => vertices[i]).Aggregate(Vec3.Zero, (sum, p) => sum + p) / split.Count; var centerId = weld.Add(center);
            for (var i = 0; i < split.Count; i++)
            {
                var a = split[i]; var b = split[(i + 1) % split.Count];
                if ((vertices[a] - center).Cross(vertices[b] - center).Length > context.Epsilon * context.Epsilon) triangles.AddRange([a, b, centerId]);
            }
        }
        return new(weld.Points.Select(p => p + origin).ToImmutableArray(), triangles.ToImmutableArray(), "Mesh " + operation);
    }
}
