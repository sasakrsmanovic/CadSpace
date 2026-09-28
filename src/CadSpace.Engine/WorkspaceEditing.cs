using System.Collections.Immutable;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Shared, undoable operations used by layout tabs and ribbon property selectors.</summary>
public static class WorkspaceEditing
{
    public static void AssignLayer(this CadSession session, string name)
    {
        if (!session.Document.Drawing.Layers.TryGetValue(name, out var layer)) throw new ArgumentException("The layer no longer exists.");
        if (layer.Locked) throw new InvalidOperationException("The destination layer is locked.");
        if (session.Selection.Count == 0) { session.CurrentLayer = name; session.Invalidate(); return; }
        var ids = session.EditableSelection().Select(e => e.Id).ToHashSet();
        session.Document.Edit("Change layer", d => d with { Entities = d.Entities.Select(e => ids.Contains(e.Id) ? e with { Layer = name } : e).ToImmutableArray() });
    }
    public static void AssignColor(this CadSession session, int aci, uint? rgb = null)
    {
        if (aci is < 0 or > 256) throw new ArgumentOutOfRangeException(nameof(aci));
        if (session.Selection.Count == 0) { session.CurrentColorIndex = aci; session.CurrentTrueColor = rgb; session.Invalidate(); return; }
        var ids = session.EditableSelection().Select(e => e.Id).ToHashSet();
        session.Document.Edit("Change color", d => d with { Entities = d.Entities.Select(e => ids.Contains(e.Id) ? e with { ColorIndex = aci, TrueColor = rgb } : e).ToImmutableArray() });
    }
    public static void AssignWeight(this CadSession session, double weight)
    {
        if (!double.IsFinite(weight) || weight is < -1 or > 2.11 || weight < 0 && weight != -1) throw new ArgumentException("Use ByLayer or a lineweight between 0 and 2.11 mm.");
        if (session.Selection.Count == 0) { session.CurrentLineWeight = weight; session.Invalidate(); return; }
        var ids = session.EditableSelection().Select(e => e.Id).ToHashSet();
        session.Document.Edit("Change lineweight", d => d with { Entities = d.Entities.Select(e => ids.Contains(e.Id) ? e with { LineWeight = weight } : e).ToImmutableArray() });
    }
    public static string AddLayout(this CadSession session)
    {
        var drawing = session.Document.Drawing; var i = 1;
        while (session.AvailableLayouts.Contains("Layout" + i, StringComparer.OrdinalIgnoreCase)) i++;
        var name = "Layout" + i; var block = "*Paper_Space" + i;
        while (drawing.Blocks.ContainsKey(block) || drawing.LayoutBlockNames.Values.Contains(block, StringComparer.OrdinalIgnoreCase)) block = "*Paper_Space" + ++i;
        session.Document.Edit("New layout", d => d with { LayoutBlockNames = d.LayoutBlockNames.Add(name, block) });
        session.ActiveLayout = name; return name;
    }
    public static void RenameLayout(this CadSession session, string name, string replacement)
    {
        if (name.Equals("Model", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Model cannot be renamed.");
        if (!session.Document.Drawing.LayoutBlockNames.TryGetValue(name, out var block)) throw new ArgumentException("The layout no longer exists.");
        if (!Linetype.ValidName(replacement) || replacement.Length > 128 || session.AvailableLayouts.Contains(replacement, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Enter a unique valid layout name.");
        if (session.Document.Drawing.Entities.Any(e => e.Layout.Equals(name, StringComparison.OrdinalIgnoreCase) && e is OpaqueEntity)) throw new NotSupportedException("This layout contains opaque source records; renaming could invalidate their references.");
        var wasActive = session.ActiveLayout.Equals(name, StringComparison.OrdinalIgnoreCase);
        session.Document.Edit("Rename layout", d => d with { LayoutBlockNames = d.LayoutBlockNames.Remove(name).Add(replacement, block),
            Entities = d.Entities.Select(e => e.Layout.Equals(name, StringComparison.OrdinalIgnoreCase) ? e with { Layout = replacement } : e).ToImmutableArray() });
        if (wasActive) session.ActiveLayout = replacement;
    }
    public static void DeleteEmptyLayout(this CadSession session, string name)
    {
        if (name.Equals("Model", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Model cannot be deleted.");
        var d = session.Document.Drawing;
        if (!d.LayoutBlockNames.TryGetValue(name, out var block)) throw new ArgumentException("The layout no longer exists.");
        if (d.Entities.Any(e => e.Layout.Equals(name, StringComparison.OrdinalIgnoreCase)) || d.Blocks.TryGetValue(block, out var definition) && !definition.Entities.IsEmpty)
            throw new InvalidOperationException("Only empty layouts can be deleted. Move or erase their objects first.");
        session.Document.Edit("Delete empty layout", s => s with { LayoutBlockNames = s.LayoutBlockNames.Remove(name) });
        if (session.ActiveLayout.Equals(name, StringComparison.OrdinalIgnoreCase)) session.ActiveLayout = "Model";
    }
}
