using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class SourceCompatibilityRegression
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new Exception("Source compatibility assertion failed."); }
        void Reject(Action action) { try { action(); } catch (FormatException) { return; } throw new Exception("Forged provenance was accepted."); }
        const string record = "0\nLWPOLYLINE\n5\nAB\n90\n2\n43\n8\n10\n0\n20\n0\n10\n100\n20\n0\n";
        const string raw = "0\nSECTION\n2\nENTITIES\n" + record + "0\nENDSEC\n0\nEOF\n";
        OpaqueEntity EarlierRecord(Entity e, string groups) => new("LWPOLYLINE", groups) {
            Id = e.Id, Handle = e.Handle, Layer = e.Layer, ColorIndex = e.ColorIndex, TrueColor = e.TrueColor,
            LineWeight = e.LineWeight, Linetype = e.Linetype, LinetypeScale = e.LinetypeScale,
            Visible = e.Visible, Layout = e.Layout
        };
        (Drawing Drawing, DxfSource Source) EarlierProject(Func<OpaqueEntity, OpaqueEntity>? change = null)
        {
            var parsed = DxfCodec.Read(raw);
            var opaque = EarlierRecord(parsed.Drawing.Entities[0], record);
            if (change != null) opaque = change(opaque);
            var original = parsed.Drawing with { Entities = [opaque] };
            return (original, parsed.Source with { Original = original });
        }
        test("older opaque width project remains readable after interpreter upgrade", () => {
            var previous = EarlierProject(); var project = CadProjectCodec.Read(CadProjectCodec.Write(previous.Drawing, previous.Source));
            Check(project.Drawing.Entities.Single() is OpaqueEntity);
            Check(DxfCodec.Write(project.Drawing, project.DxfSource).Text == raw);
            var reopened = CadProjectCodec.Read(CadProjectCodec.Write(project.Drawing, project.DxfSource));
            Check(DxfCodec.Write(reopened.Drawing, reopened.DxfSource).Text == raw);
        });
        test("unrelated native edits preserve an older opaque width record", () => {
            var previous = EarlierProject(); var drawing = previous.Drawing with { Entities = previous.Drawing.Entities.Add(new PointEntity(new(20, 30))) };
            var project = CadProjectCodec.Read(CadProjectCodec.Write(drawing, previous.Source));
            var output = DxfCodec.Read(DxfCodec.Write(project.Drawing, project.DxfSource).Text).Drawing;
            Check(output.Entities.OfType<PolylineEntity>().Single().ConstantWidth == 8 && output.Entities.OfType<PointEntity>().Count() == 1);
        });
        test("older opaque block child is checked against its own source record", () => {
            var d = Drawing.Empty;
            d = d with { Blocks = d.Blocks.Add("B", new("B", default, [PolylineEntity.FromPoints([default, new(100, 0)]) with { ConstantWidth = 8 }])), Entities = [new BlockReferenceEntity("B", default, new(1, 1, 1))] };
            var parsed = DxfCodec.Read(DxfCodec.Write(d).Text); var child = parsed.Drawing.Blocks["B"].Entities[0];
            var groups = string.Concat(parsed.Source.Records[child.Id].Select(p => $"{p.Code}\n{p.Value}\n"));
            var old = parsed.Drawing with { Blocks = parsed.Drawing.Blocks.SetItem("B", parsed.Drawing.Blocks["B"] with { Entities = [EarlierRecord(child, groups)] }) };
            var project = CadProjectCodec.Read(CadProjectCodec.Write(old, parsed.Source with { Original = old }));
            Check(project.Drawing.Blocks["B"].Entities.Single() is OpaqueEntity);
            Check(DxfCodec.Write(project.Drawing, project.DxfSource).Text == parsed.Source.Text);
        });
        test("opaque compatibility rejects altered raw groups", () => {
            var previous = EarlierProject(e => e with { RawRecord = e.RawRecord.Replace("43\n8\n", "43\n9\n") });
            Reject(() => CadProjectCodec.Read(CadProjectCodec.Write(previous.Drawing, previous.Source)));
        });
        test("opaque compatibility rejects altered common properties", () => {
            var previous = EarlierProject(e => e with { ColorIndex = 1 });
            Reject(() => CadProjectCodec.Read(CadProjectCodec.Write(previous.Drawing, previous.Source)));
        });
        test("opaque compatibility rejects a forged record type", () => {
            var previous = EarlierProject(e => e with { DxfType = "LINE" });
            Reject(() => CadProjectCodec.Read(CadProjectCodec.Write(previous.Drawing, previous.Source)));
        });
    }
}
