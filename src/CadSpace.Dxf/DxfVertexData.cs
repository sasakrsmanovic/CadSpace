using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

/// <summary>Single-pass DXF polyline vertex decoding, without per-vertex slices or suffix scans.</summary>
internal static class DxfVertexData
{
    public static ImmutableArray<PolyVertex> Polyline(ReadOnlySpan<DxfPair> pairs, double elevation)
    {
        var result = ImmutableArray.CreateBuilder<PolyVertex>();
        double x = 0, y = 0, bulge = 0, start = 0, end = 0;
        bool active = false, hasY = false;
        void Commit()
        {
            if (!active) return;
            if (!hasY) throw new FormatException("A polyline vertex has no Y coordinate.");
            if (result.Count >= 1000000) throw new FormatException("Polyline exceeds one million vertices.");
            result.Add(new(new(x, y, elevation), bulge) { StartWidth = start, EndWidth = end });
        }
        foreach (var pair in pairs)
        {
            // Appended XDATA is not part of the vertex sequence.
            if (pair.Code >= 1000) break;
            if (pair.Code == 10)
            {
                Commit(); x = Number(pair.Value); y = bulge = start = end = 0; active = true; hasY = false;
            }
            else if (active) switch (pair.Code)
            {
                case 20: y = Number(pair.Value); hasY = true; break;
                case 42: bulge = Number(pair.Value); break;
                case 40: start = Number(pair.Value); break;
                case 41: end = Number(pair.Value); break;
            }
        }
        Commit(); return result.ToImmutable();
    }
    private static double Number(string text) => GeometryMath.Number(text, out var n) ? n : throw new FormatException("Invalid DXF vertex number.");
}
