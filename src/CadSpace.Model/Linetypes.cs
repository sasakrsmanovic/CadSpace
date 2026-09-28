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
        ArgumentNullException.ThrowIfNull(type);
        if (!ValidName(type.Name) || type.Description == null || type.Elements.IsDefault || type.Description.IndexOfAny(['\0', '\r', '\n']) >= 0 || type.Elements.Length > 64 || type.Elements.Any(e => !double.IsFinite(e))) throw new ArgumentException("Invalid linetype definition; at most 64 finite elements are supported.");
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
        if (!double.IsFinite(distance) || !double.IsFinite(dotRadius) || dotRadius < 0) throw new ArgumentException("Pattern positions and dot radius must be finite.");
        var length = CheckedPeriod(); var phase = ((distance % length) + length) % length; double cursor = 0;
        foreach (var value in Definition.Elements)
        {
            var width = Math.Abs(value) * Scale;
            if (value > 0 && phase >= cursor && phase < cursor + width) return true;
            if (value == 0 && Math.Min(Math.Abs(phase - cursor), length - Math.Abs(phase - cursor)) <= dotRadius) return true;
            cursor += width;
        }
        return false;
    }
    private double CheckedPeriod()
    {
        Linetype.Validate(Definition);
        var period = Length;
        if (Definition.IsComplex || Definition.Elements.IsEmpty || !double.IsFinite(Scale) || Scale <= 0 || !double.IsFinite(period) || period < 1e-9 || period > 1e15)
            throw new ArgumentException("A stroke pattern requires a finite, nonempty simple definition and a supported positive scale.");
        return period;
    }
    public readonly record struct Stroke(Vec3 Start, Vec3 End, bool Dot);
    /// <summary>Clip before expanding dashes: a billion-unit line does not allocate a billion dash segments.</summary>
    public IEnumerable<Stroke> VisibleStrokes(ScenePath path, Bounds3 view, int budget = 100000)
    {
        ArgumentNullException.ThrowIfNull(path);
        var period = CheckedPeriod(); var phase = 0.0; var emitted = 0;
        if (budget < 1 || budget > 1_000_000) throw new ArgumentOutOfRangeException(nameof(budget));
        if (!double.IsFinite(view.Min.X) || !double.IsFinite(view.Min.Y) || !double.IsFinite(view.Max.X) || !double.IsFinite(view.Max.Y) || view.Min.X > view.Max.X || view.Min.Y > view.Max.Y)
            throw new ArgumentException("A finite, ordered visible XY box is required.");
        var edges = path.Points.Length - (path.Closed ? 0 : 1);
        for (var i = 0; i < edges; i++)
        {
            var a = path.Points[i]; var b = path.Points[(i + 1) % path.Points.Length]; var d = b - a; var length = d.Length;
            if (!a.IsFinite || !b.IsFinite || !double.IsFinite(length)) throw new ArgumentException("Pattern path coordinates and edge lengths must be finite.");
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
                var begin = lo * length;
                // Work relative to the clipped start. An absolute cycle number can exceed 2^53,
                // where incrementing a double by one no longer advances and an all-gap loop hangs.
                var extent = Math.Max(0, (hi - lo) * length);
                var localPhase = (phase + begin % period) % period;
                var cycles = Math.Floor((localPhase + extent) / period) + 1;
                if (!double.IsFinite(cycles) || cycles > budget) throw new InvalidOperationException("Visible linetype pattern exceeds the stroke budget.");
                var origin = a + d * lo; var unit = d / length;
                for (var cycle = 0; cycle < (int)cycles; cycle++)
                {
                    var cursor = cycle * period - localPhase;
                    foreach (var value in Definition.Elements)
                    {
                        var next = cursor + Math.Abs(value) * Scale;
                        if ((value > 0 && next > 0 && cursor < extent) || (value == 0 && cursor >= 0 && cursor <= extent))
                        {
                            if (++emitted > budget) throw new InvalidOperationException("Visible linetype pattern exceeds the stroke budget.");
                            yield return new(origin + unit * Math.Max(0, cursor), origin + unit * Math.Min(extent, next), value == 0);
                        }
                        cursor = next;
                    }
                }
            }
            phase = (phase + length % period) % period;
        }
    }
}
