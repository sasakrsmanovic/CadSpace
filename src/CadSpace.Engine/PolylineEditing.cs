using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Atomic preselection-based PEDIT subset preserving IDs, widths, bulges and source styles.</summary>
public static class PolylineEditing
{
    public static void EditPolylineVertex(this CadSession session, PolylineEntity expected, int index, PolyVertex replacement)
    {
        if (index < 0 || index >= expected.Vertices.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected))) throw new InvalidOperationException("The selected polyline changed; reselect it before editing.");
        if (replacement.Position != expected.Vertices[index].Position) throw new ArgumentException("This editor changes segment properties, not vertex coordinates.");
        // Materialize a former global width on the other segments before switching to variable widths.
        var vertices = expected.ConstantWidth > 0 ? expected.Vertices.Select(v => v with { StartWidth = expected.ConstantWidth, EndWidth = expected.ConstantWidth }).ToImmutableArray() : expected.Vertices;
        var next = expected with { ConstantWidth = 0, Vertices = vertices.SetItem(index, replacement) };
        session.Document.Edit("Polyline segment properties", d => d with { Entities = d.Entities.Select(e => e.Id == expected.Id ? next : e).ToImmutableArray() });
    }
    public static void SetWidth(this CadSession session, double width)
    {
        if (!double.IsFinite(width) || width < 0 || width > 1e12) throw new ArgumentException("Width must be between 0 and 1e12.");
        Edit(session, "Polyline width", p => p with { ConstantWidth = width, Vertices = p.Vertices.Select(v => v with { StartWidth = 0, EndWidth = 0 }).ToImmutableArray() });
    }
    public static void SetClosed(this CadSession session, bool closed) => Edit(session, closed ? "Close polylines" : "Open polylines", p =>
    {
        if (closed && p.Vertices.Length < 3 && p.Vertices.All(v => v.Bulge == 0)) throw new ArgumentException("Closing a straight polyline requires three vertices.");
        return p with { Closed = closed };
    });
    public static void Reverse(this CadSession session) => Edit(session, "Reverse polylines", Reverse);
    public static PolylineEntity Reverse(PolylineEntity polyline)
    {
        var vertices = ImmutableArray.CreateBuilder<PolyVertex>(polyline.Vertices.Length);
        for (var i = polyline.Vertices.Length - 1; i >= 0; i--)
        {
            var prior = i > 0 ? i - 1 : polyline.Closed ? polyline.Vertices.Length - 1 : -1;
            var source = prior >= 0 ? polyline.Vertices[prior] : polyline.Vertices[^1];
            // The last open vertex has no outgoing segment; retain its dormant width/bulge for a double reversal.
            vertices.Add(new(polyline.Vertices[i].Position, prior >= 0 ? -source.Bulge : source.Bulge)
                { StartWidth = prior >= 0 ? source.EndWidth : source.StartWidth, EndWidth = prior >= 0 ? source.StartWidth : source.EndWidth });
        }
        return polyline with { Vertices = vertices.ToImmutable() };
    }
    private static void Edit(CadSession session, string name, Func<PolylineEntity, PolylineEntity> change)
    {
        var selected = session.EditableSelection(); var replacements = new Dictionary<Guid, Entity>();
        Entity Apply(Entity entity) => entity switch
        {
            PolylineEntity polyline => change(polyline),
            PlacedEntity placed => placed with { Geometry = Apply(placed.Geometry) },
            _ => throw new ArgumentException("Select only 2D polylines, including OCS/affine placements.")
        };
        foreach (var entity in selected) replacements.Add(entity.Id, Apply(entity));
        session.Document.Edit(name, d => d with { Entities = d.Entities.Select(e => replacements.GetValueOrDefault(e.Id) ?? e).ToImmutableArray() });
    }
}
