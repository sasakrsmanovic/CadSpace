using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public readonly record struct PickResult(Guid EntityId, Vec3 Point, double Distance);

/// <summary>Double-precision picking of faces, visible edges, points and text planes; independent of a UI or graphics API.</summary>
public static class ScenePicking
{
    public static PickResult? Pick(DrawingScene scene, Ray3 ray, Func<Vec3, Vec3> project, Vec3 screen, double tolerance = 6, Plane3? clip = null, bool wireframe = false)
    {
        if (!ray.Origin.IsFinite || !ray.Direction.IsFinite || ray.Direction.Length < 1e-15 || !double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentException("Invalid picking ray or tolerance.");
        ray = ray with { Direction = ray.Direction.Normalized };
        bool Kept(Vec3 p) => clip is not Plane3 plane || plane.SignedDistance(p) <= 1e-8;
        PickResult? result = null;
        var front = double.PositiveInfinity;
        var acceleration = SceneAcceleration.For(scene);
        var candidates = new List<int>();
        if (!wireframe) acceleration.Triangles.Query(ray, candidates);
        foreach (var index in candidates)
        {
            var triangle = scene.Triangles[index];
            if (ray.IntersectTriangle(triangle.A, triangle.B, triangle.C, out var distance) && distance < front && Kept(ray.At(distance)))
            {
                front = distance; result = new(triangle.EntityId, ray.At(distance), distance);
            }
        }
        var bestPixelDistance = tolerance;
        void Candidate(Guid id, Vec3 point)
        {
            var p = project(point); var depth = (point - ray.Origin).Dot(ray.Direction);
            if (!p.IsFinite || p.Z <= 0 || depth < 0 || !Kept(point)) return;
            if (depth > front + Math.Max(1e-7, front * 1e-7)) return;
            var dx = p.X - screen.X; var dy = p.Y - screen.Y; var pixels = Math.Sqrt(dx * dx + dy * dy);
            if (pixels <= bestPixelDistance) { bestPixelDistance = pixels; result = new(id, point, depth); }
        }
        candidates.Clear();
        acceleration.Paths.Visit(b => SceneAcceleration.NearScreen(b, project, screen, tolerance), candidates);
        candidates.Sort();
        foreach (var index in candidates)
        {
            var path = scene.Paths[index];
            if (path.Points.Length == 1) { Candidate(path.EntityId, path.Points[0]); continue; }
            for (var i = 0; i < path.Points.Length - (path.Closed ? 0 : 1); i++)
            {
                var a = path.Points[i]; var b = path.Points[(i + 1) % path.Points.Length];
                if (clip is Plane3 plane)
                {
                    var da = plane.SignedDistance(a); var db = plane.SignedDistance(b);
                    if (da > 0 && db > 0) continue;
                    if ((da > 0) != (db > 0)) { var intersection = Vec3.Lerp(a, b, da / (da - db)); if (da > 0) a = intersection; else b = intersection; }
                }
                var segment = b - a; var w = ray.Origin - a; var c = segment.Dot(segment);
                if (c <= 1e-24) { Candidate(path.EntityId, a); continue; }
                var dot = ray.Direction.Dot(segment); var denominator = c - dot * dot;
                var t = denominator > c * 1e-14 ? (segment.Dot(w) - dot * ray.Direction.Dot(w)) / denominator : 0;
                Candidate(path.EntityId, a + segment * Math.Clamp(t, 0, 1));
            }
        }
        candidates.Clear(); acceleration.Texts.Query(ray, candidates); candidates.Sort();
        foreach (var index in candidates)
        {
            var text = scene.Texts[index];
            var normal = text.AxisX.Cross(text.AxisY);
            if (!ray.IntersectPlane(text.Position, normal, out var hit) || !Kept(hit)) continue;
            var distance = (hit - ray.Origin).Dot(ray.Direction); if (distance > front + 1e-7) continue;
            var d = (hit - text.Position) / text.Height; var x = text.AxisX; var y = text.AxisY;
            var determinant = x.Dot(x) * y.Dot(y) - x.Dot(y) * x.Dot(y); if (determinant <= 1e-20) continue;
            var u = (d.Dot(x) * y.Dot(y) - d.Dot(y) * x.Dot(y)) / determinant;
            var v = (d.Dot(y) * x.Dot(x) - d.Dot(x) * x.Dot(y)) / determinant;
            var lines = text.Text.Split('\n');
            if (u >= 0 && u <= lines.Max(l => Math.Max(1, l.Length)) * .65 && v >= -.3 - (lines.Length - 1) * 1.3 && v <= 1)
                result = new(text.EntityId, hit, distance);
        }
        return result;
    }
}
