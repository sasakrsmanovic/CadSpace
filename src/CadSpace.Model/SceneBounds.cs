using System.Runtime.CompilerServices;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Identity-keyed cached bounds. Record copies never inherit another scene's cached geometry.</summary>
internal static class SceneBounds
{
    private sealed class Box(DrawingScene scene)
    {
        public Bounds3 Value { get; } = Measure(scene);
    }
    private static readonly ConditionalWeakTable<DrawingScene, Box> Cache = new();
    public static Bounds3 For(DrawingScene scene) => Cache.GetValue(scene, static s => new(s)).Value;
    private static Bounds3 Measure(DrawingScene scene)
    {
        var bounds = Bounds3.Empty;
        foreach (var path in scene.Paths) foreach (var point in path.Points) bounds = bounds.Include(point);
        foreach (var text in scene.Texts)
        {
            bounds = bounds.Include(text.Position);
            // Preserve the original fit semantics. Picking uses its separate conservative text bounds.
            bounds = bounds.Include(text.Position + text.AxisX * (text.Height * Math.Max(1, text.Text.Length) * .65) + text.AxisY * text.Height);
        }
        foreach (var triangle in scene.Triangles) bounds = bounds.Include(triangle.A).Include(triangle.B).Include(triangle.C);
        return bounds;
    }
}
