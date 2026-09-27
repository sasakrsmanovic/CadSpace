using System.Collections.Immutable;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Undoable layer and linetype operations shared by dialogs, automation and non-UI hosts.</summary>
public static class DrawingStyles
{
    public static void LoadLinetype(this CadSession session, Linetype type)
    {
        Linetype.Validate(type);
        if (type.Name.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) || type.Name.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase) || type.Name.Equals("CONTINUOUS", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Reserved linetypes cannot be replaced.");
        if (type.IsComplex) throw new NotSupportedException("Complex text/shape definitions must be imported with their DXF provenance.");
        session.Document.Edit("Define linetype", d => d with { Linetypes = d.Linetypes.SetItem(type.Name, type) });
    }
    public static void SetCurrentLinetype(this CadSession session, string name)
    {
        if (!name.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) && !name.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase) && !session.Document.Drawing.Linetypes.ContainsKey(name))
        {
            var builtIn = Linetype.BuiltIns.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Load or define that linetype first.");
            session.LoadLinetype(builtIn);
        }
        session.CurrentLinetype = name; session.Invalidate();
    }
    public static void SetSelectedLinetype(this CadSession session, string name, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 || scale > 1e9) throw new ArgumentException("Linetype scale must be positive and no larger than 1e9.");
        if (!name.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) && !name.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase) && !session.Document.Drawing.Linetypes.ContainsKey(name)) throw new ArgumentException("Unknown linetype.");
        var selected = session.EditableSelection().Select(e => e.Id).ToHashSet();
        session.Document.Edit("Linetype properties", d => d with { Entities = d.Entities.Select(e => selected.Contains(e.Id) ? e with { Linetype = name, LinetypeScale = scale } : e).ToImmutableArray() });
    }
    public static void AddLayer(this CadSession session, string name)
    {
        if (!Linetype.ValidName(name) || session.Document.Drawing.Layers.ContainsKey(name)) throw new ArgumentException("Enter a unique valid layer name.");
        session.Document.Edit("New layer", d => d with { Layers = d.Layers.Add(name, new(name)) });
    }
    public static void UpdateLayer(this CadSession session, string oldName, Layer layer)
    {
        if (!session.Document.Drawing.Layers.ContainsKey(oldName)) throw new ArgumentException("The layer no longer exists.");
        if (!Linetype.ValidName(layer.Name)) throw new ArgumentException("Invalid layer name.");
        if (!session.Document.Drawing.Linetypes.ContainsKey(layer.Linetype)) throw new ArgumentException("Load the layer linetype first.");
        var rename = !string.Equals(oldName, layer.Name, StringComparison.Ordinal);
        if (rename && oldName == "0") throw new ArgumentException("Layer 0 cannot be renamed.");
        if (rename && !oldName.Equals(layer.Name, StringComparison.OrdinalIgnoreCase) && session.Document.Drawing.Layers.ContainsKey(layer.Name)) throw new ArgumentException("Layer name already exists.");
        Entity Replace(Entity entity)
        {
            if (entity is PlacedEntity placed) entity = placed with { Geometry = Replace(placed.Geometry) };
            else if (entity is CompositeEntity group) entity = group with { Children = group.Children.Select(Replace).ToImmutableArray() };
            if (entity.Layer.Equals(oldName, StringComparison.OrdinalIgnoreCase))
            {
                if (entity is OpaqueEntity) throw new NotSupportedException("Renaming a layer used by opaque DXF objects would leave inconsistent raw records.");
                entity = entity with { Layer = layer.Name };
            }
            return entity;
        }
        session.Document.Edit(rename ? "Rename layer" : "Layer properties", d => d with {
            Layers = d.Layers.Remove(oldName).Add(layer.Name, layer),
            Entities = rename ? d.Entities.Select(Replace).ToImmutableArray() : d.Entities,
            Blocks = rename ? d.Blocks.ToImmutableDictionary(p => p.Key, p => p.Value with { Entities = p.Value.Entities.Select(Replace).ToImmutableArray() }, StringComparer.OrdinalIgnoreCase) : d.Blocks
        });
        if (session.CurrentLayer.Equals(oldName, StringComparison.OrdinalIgnoreCase)) session.CurrentLayer = layer.Name;
        session.Invalidate();
    }
    public static void DeleteLayer(this CadSession session, string name)
    {
        if (name == "0" || name.Equals(session.CurrentLayer, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Layer 0 and the current layer cannot be deleted.");
        bool Used(Entity e) => e.Layer.Equals(name, StringComparison.OrdinalIgnoreCase) || e is PlacedEntity p && Used(p.Geometry) || e is CompositeEntity c && c.Children.Any(Used);
        if (session.Document.Drawing.Entities.Any(Used) || session.Document.Drawing.Blocks.Values.Any(b => b.Entities.Any(Used))) throw new ArgumentException("The layer is used by drawing or block geometry.");
        session.Document.Edit("Delete layer", d => d with { Layers = d.Layers.Remove(name) });
    }
}
