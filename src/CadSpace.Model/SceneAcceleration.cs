using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Identity-scoped lazy indexes; record copies cannot accidentally reuse stale bounds.</summary>
public sealed class SceneAcceleration
{
    private static readonly ConditionalWeakTable<DrawingScene, SceneAcceleration> Cache = new();
    public static SceneAcceleration For(DrawingScene scene) => Cache.GetValue(scene, s => new(s));
    private readonly Lazy<SpatialIndex> _paths, _texts, _triangles;
    public SpatialIndex Paths => _paths.Value;
    public SpatialIndex Texts => _texts.Value;
    public SpatialIndex Triangles => _triangles.Value;
    public Bounds3 Bounds { get; }
    private SceneAcceleration(DrawingScene scene)
    {
        // Empty paths are legal. Give them point bounds; the narrow phase simply skips them.
        var paths = scene.Paths.Select(p => p.Points.IsEmpty ? new Bounds3(default, default) : Bounds3.From(p.Points)).ToArray();
        var texts = scene.Texts.Select(TextBounds).ToArray();
        _paths = new(() => new(paths)); _texts = new(() => new(texts));
        _triangles = new(() => new(scene.Triangles.Select(t => Bounds3.Empty.Include(t.A).Include(t.B).Include(t.C))));
        var b = Bounds3.Empty;
        for (var i = 0; i < paths.Length; i++) if (!scene.Paths[i].Points.IsEmpty) b = b.Union(paths[i]);
        foreach (var box in texts) b = b.Union(box);
        foreach (var t in scene.Triangles) b = b.Include(t.A).Include(t.B).Include(t.C);
        Bounds = b;
    }
    public static Bounds3 TextBounds(SceneText text)
    {
        var lines = text.Text.Split('\n');
        var x = text.AxisX * (text.Height * Math.Max(1, lines.Max(l => l.Length)) * 1.5);
        var top = text.AxisY * text.Height;
        var bottom = text.AxisY * (-text.Height * (.3 + (lines.Length - 1) * 1.3));
        return Bounds3.Empty.Include(text.Position + top).Include(text.Position + top + x).Include(text.Position + bottom).Include(text.Position + bottom + x);
    }
    public static bool NearScreen(Bounds3 b, Func<Vec3, Vec3> project, Vec3 screen, double tolerance)
    {
        var projected = Bounds3.Empty;
        for (var i = 0; i < 8; i++)
        {
            var p = project(new((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z));
            // Boxes crossing the eye plane require conservative traversal.
            if (!p.IsFinite || p.Z <= 0) return true;
            projected = projected.Include(p);
        }
        return screen.X + tolerance >= projected.Min.X && screen.X - tolerance <= projected.Max.X && screen.Y + tolerance >= projected.Min.Y && screen.Y - tolerance <= projected.Max.Y;
    }
}

/// <summary>Session-owned incremental tessellation. Unchanged immutable root entities reuse their paths, text and triangles.</summary>
public sealed class DrawingSceneCache
{
    private readonly Dictionary<Guid, (Entity Entity, DrawingScene Scene)> _roots = new();
    private Drawing? _drawing;
    private string _layout = "";
    private DrawingScene? _scene;
    public int RebuiltRoots { get; private set; }
    public int ReusedRoots { get; private set; }
    public DrawingScene Build(Drawing drawing, string layout = "Model")
    {
        if (_drawing != null && _drawing.Entities == drawing.Entities && ReferenceEquals(_drawing.Blocks, drawing.Blocks) && ReferenceEquals(_drawing.Layers, drawing.Layers) && layout == _layout && _scene != null) return _scene;
        var reset = _drawing == null || !ReferenceEquals(_drawing.Blocks, drawing.Blocks) || !ReferenceEquals(_drawing.Layers, drawing.Layers) || layout != _layout;
        var next = new Dictionary<Guid, (Entity Entity, DrawingScene Scene)>();
        var paths = ImmutableArray.CreateBuilder<ScenePath>(); var text = ImmutableArray.CreateBuilder<SceneText>(); var triangles = ImmutableArray.CreateBuilder<SceneTriangle>();
        RebuiltRoots = ReusedRoots = 0; long vertices = 0;
        foreach (var e in drawing.Entities)
        {
            if (!e.Visible || !e.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase) || !drawing.LayerFor(e).Visible) continue;
            DrawingScene part;
            if (!reset && _roots.TryGetValue(e.Id, out var previous) && ReferenceEquals(previous.Entity, e)) { part = previous.Scene; ReusedRoots++; }
            else { part = EntityGeometry.BuildScene(drawing with { Entities = [e] }, layout); RebuiltRoots++; }
            vertices += part.Paths.Sum(p => (long)p.Points.Length);
            if (vertices > 2_000_000 || triangles.Count + (long)part.Triangles.Length > 1_000_000) throw new ArgumentException("Expanded scene exceeds the geometry budget.");
            next[e.Id] = (e, part); paths.AddRange(part.Paths); text.AddRange(part.Texts); triangles.AddRange(part.Triangles);
        }
        _roots.Clear(); foreach (var pair in next) _roots.Add(pair.Key, pair.Value);
        _drawing = drawing; _layout = layout;
        return _scene = new(paths.ToImmutable(), text.ToImmutable(), triangles.ToImmutable());
    }
}
