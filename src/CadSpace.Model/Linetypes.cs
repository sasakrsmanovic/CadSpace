using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Simple DXF pattern: positive dash, negative gap, zero dot. Complex styles remain source data.</summary>
public sealed record Linetype(string Name, string Description, ImmutableArray<double> Elements, bool IsComplex = false)
{
    public double Length => Elements.Sum(Math.Abs);
    public static ImmutableDictionary<string, Linetype> Defaults { get; } = ImmutableDictionary.Create<string, Linetype>(StringComparer.OrdinalIgnoreCase)
        .Add("CONTINUOUS", new("CONTINUOUS", "Solid line", []));
    public static IReadOnlyList<Linetype> BuiltIns { get; } = new Linetype[] {
        new("DASHED", "Dashed __ __ __", [12, -6]), new("HIDDEN", "Hidden _ _ _", [6, -3]),
        new("CENTER", "Center ____ _ ____", [24, -6, 6, -6]), new("DASHDOT", "Dash dot __ . __", [12, -4, 0, -4]),
        new("DOT", "Dotted . . .", [0, -5]) };
    public static bool Equivalent(Linetype a, Linetype b) => a.Name == b.Name && a.Description == b.Description && a.IsComplex == b.IsComplex && a.Elements.SequenceEqual(b.Elements);
    public static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && name.IndexOfAny(['\0', '\r', '\n', '<', '>', '/', '\\', ':', ';', '?', '*', '|', '=']) < 0;
    public static void Validate(Linetype type)
    {
        if (!ValidName(type.Name) || type.Description.IndexOfAny(['\0', '\r', '\n']) >= 0 || type.Elements.Length > 64 || type.Elements.Any(e => !double.IsFinite(e))) throw new ArgumentException("Invalid linetype definition; at most 64 finite elements are supported.");
        if (!type.Elements.IsEmpty && (type.Length < 1e-9 || type.Length > 1e12)) throw new ArgumentException("Linetype period must be between 1e-9 and 1e12 drawing units.");
    }
    public static string ResolveName(string requested, Layer layer, string? inherited = null) => requested.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) ? layer.Linetype
        : requested.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase) ? inherited ?? "CONTINUOUS" : requested;
}

/// <summary>Immutable lightweight pattern reference, shared across drawing paths without splitting geometry.</summary>
public sealed record StrokePattern(Linetype Definition, double Scale)
{
    public double Length => Definition.Length * Scale;
    public bool IsInk(double distance, double dotRadius = 0)
    {
        var length = Length; var phase = ((distance % length) + length) % length; double cursor = 0;
        foreach (var value in Definition.Elements)
        {
            var width = Math.Abs(value) * Scale;
            if (value > 0 && phase >= cursor && phase < cursor + width) return true;
            if (value == 0 && Math.Min(Math.Abs(phase - cursor), length - Math.Abs(phase - cursor)) <= dotRadius) return true;
            cursor += width;
        }
        return false;
    }
    public readonly record struct Stroke(Vec3 Start, Vec3 End, bool Dot);
    /// <summary>Clip before expanding dashes: a billion-unit line does not allocate a billion dash segments.</summary>
    public IEnumerable<Stroke> VisibleStrokes(ScenePath path, Bounds3 view, int budget = 100000)
    {
        var period = Length; var phase = 0.0; var emitted = 0;
        var edges = path.Points.Length - (path.Closed ? 0 : 1);
        for (var i = 0; i < edges; i++)
        {
            var a = path.Points[i]; var b = path.Points[(i + 1) % path.Points.Length]; var d = b - a; var length = d.Length;
            if (length < 1e-12) continue;
            double lo = 0, hi = 1;
            bool Slab(double origin, double delta, double min, double max)
            {
                if (Math.Abs(delta) < 1e-30) return origin >= min && origin <= max;
                var p = (min - origin) / delta; var q = (max - origin) / delta;
                if (p > q) (p, q) = (q, p); lo = Math.Max(lo, p); hi = Math.Min(hi, q); return lo <= hi;
            }
            if (Slab(a.X, d.X, view.Min.X, view.Max.X) && Slab(a.Y, d.Y, view.Min.Y, view.Max.Y))
            {
                var begin = lo * length; var end = hi * length;
                var first = Math.Floor((phase + begin) / period); var last = Math.Floor((phase + end) / period);
                if (last - first > budget) throw new InvalidOperationException("Visible linetype pattern exceeds the stroke budget.");
                for (var cycle = first; cycle <= last; cycle++)
                {
                    var cursor = cycle * period - phase;
                    foreach (var value in Definition.Elements)
                    {
                        var next = cursor + Math.Abs(value) * Scale;
                        if ((value > 0 && next > begin && cursor < end) || (value == 0 && cursor >= begin && cursor <= end))
                        {
                            if (++emitted > budget) throw new InvalidOperationException("Visible linetype pattern exceeds the stroke budget.");
                            yield return new(a + d * (Math.Max(begin, cursor) / length), a + d * (Math.Min(end, next) / length), value == 0);
                        }
                        cursor = next;
                    }
                }
            }
            phase = (phase + length) % period;
        }
    }
}
