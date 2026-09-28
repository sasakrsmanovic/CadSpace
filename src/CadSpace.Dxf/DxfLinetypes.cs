using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Model;

namespace CadSpace.Dxf;

internal static class DxfLinetypes
{
    private static string Text(ImmutableArray<DxfPair> r, int code, string fallback = "") => r.FirstOrDefault(p => p.Code == code).Value ?? fallback;
    public static Drawing Read(Drawing drawing, ImmutableArray<DxfPair> tables, ImmutableArray<DxfPair> header, Action<string> warn)
    {
        var types = drawing.Linetypes;
        foreach (var record in DxfEntityReader.Records(tables).Where(r => Text(r, 0) == "LTYPE"))
        {
            var name = Text(record, 2);
            if (name.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) || name.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var elements = record.Where(p => p.Code == 49).Select(p => double.Parse(p.Value, CultureInfo.InvariantCulture)).ToImmutableArray();
                if (int.Parse(Text(record, 73, "0"), CultureInfo.InvariantCulture) != elements.Length) throw new FormatException("LTYPE element count mismatch.");
                var complex = record.Any(p => p.Code == 74 && p.Value.Trim() != "0");
                var type = new Linetype(name, Text(record, 3), elements, complex); Linetype.Validate(type);
                var declared = double.Parse(Text(record, 40, "0"), CultureInfo.InvariantCulture);
                if (Math.Abs(declared - type.Length) > 1e-8 * Math.Max(1, type.Length)) throw new FormatException("LTYPE pattern length mismatch.");
                types = types.SetItem(name, type);
                if (complex) warn($"Complex linetype {name}: shape/text components remain in source data; displayed continuously.");
            }
            catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
            {
                // Keep the original table entry. An opaque style is not rewritten as an empty simple pattern.
                if (Linetype.ValidName(name)) types = types.SetItem(name, new(name, "Unsupported source definition", [], true));
                warn($"Linetype {name} retained as source data: {error.Message}");
            }
        }
        double scale = 1;
        for (var i = 0; i + 1 < header.Length; i++) if (header[i].Code == 9 && header[i].Value == "$LTSCALE")
        {
            scale = double.Parse(header[i + 1].Value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(scale) || scale <= 0 || scale > 1e9) throw new FormatException("Invalid global LTSCALE.");
        }
        return drawing with { Linetypes = types, LinetypeScale = scale };
    }
    public static IEnumerable<ImmutableArray<DxfPair>> Write(Drawing drawing, DxfSource? source, IEnumerable<ImmutableArray<DxfPair>> old, Func<string> handle)
    {
        var originals = old.ToDictionary(r => Text(r, 2), StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "BYBLOCK", "BYLAYER" })
            yield return originals.TryGetValue(name, out var reserved) ? reserved : Encode(new(name, "", []), handle());
        foreach (var type in drawing.Linetypes.Values)
        {
            if (originals.TryGetValue(type.Name, out var record) && source?.Original.Linetypes.TryGetValue(type.Name, out var before) == true && Linetype.Equivalent(type, before))
            { yield return record; continue; }
            if (type.IsComplex) throw new NotSupportedException($"Complex linetype {type.Name} requires its original DXF provenance. Save a native project instead.");
            yield return Encode(type, originals.TryGetValue(type.Name, out record) ? Text(record, 5, handle()) : handle());
        }
    }
    private static ImmutableArray<DxfPair> Encode(Linetype type, string handle)
    {
        var b = ImmutableArray.CreateBuilder<DxfPair>();
        void Pair(int code, object value) => b.Add(new(code, Convert.ToString(value, CultureInfo.InvariantCulture)!));
        Pair(0, "LTYPE"); Pair(5, handle); Pair(100, "AcDbSymbolTableRecord"); Pair(100, "AcDbLinetypeTableRecord");
        Pair(2, type.Name); Pair(70, 0); Pair(3, type.Description); Pair(72, 65); Pair(73, type.Elements.Length); Pair(40, type.Length);
        foreach (var element in type.Elements) { Pair(49, element); Pair(74, 0); }
        return b.ToImmutable();
    }
}
