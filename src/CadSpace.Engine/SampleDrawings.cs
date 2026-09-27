using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public static class SampleDrawings
{
    public static Drawing StudioPlan()
    {
        var drawing = Drawing.Empty with { Name = "Design studio.dxf" };
        foreach (var layer in new Layer[] { new("A-WALL", 0xFFDCE5EC, LineWeight: 0.5), new("A-GLAZ", 0xFF71BDDC), new("A-DOOR", 0xFFB7D4DE), new("A-FURN", 0xFFCFB278), new("A-DIMS", 0xFF8FAC98), new("A-TEXT", 0xFFCFD8E2) }) drawing = drawing with { Layers = drawing.Layers.Add(layer.Name, layer) };
        var e = new List<Entity>();
        void Rect(double x, double y, double w, double h, string layer) => e.Add(PolylineEntity.FromPoints(new Vec3[] { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h) }, true) with { Layer = layer });
        void Line(double x1, double y1, double x2, double y2, string layer = "A-WALL") => e.Add(new LineEntity(new(x1, y1), new(x2, y2)) { Layer = layer });
        void Text(double x, double y, string text, double height = 10) => e.Add(new TextEntity(new(x, y), text, height) { Layer = "A-TEXT" });
        Rect(0, 0, 840, 480, "A-WALL"); Rect(10, 10, 820, 460, "A-WALL");
        Line(290, 10, 290, 310); Line(298, 10, 298, 310); Line(290, 370, 290, 470); Line(298, 370, 298, 470);
        Line(560, 10, 560, 100); Line(568, 10, 568, 100); Line(560, 160, 560, 470); Line(568, 160, 568, 470);
        Line(10, 230, 90, 230); Line(150, 230, 290, 230); Line(10, 238, 90, 238); Line(150, 238, 290, 238);
        Line(568, 270, 680, 270); Line(740, 270, 830, 270); Line(568, 278, 680, 278); Line(740, 278, 830, 278);
        foreach (var x in new[] { 50, 190, 370, 510, 650, 770 }) { Rect(x, 0, 70, 10, "A-GLAZ"); Line(x, 5, x + 70, 5, "A-GLAZ"); Rect(x, 470, 70, 10, "A-GLAZ"); Line(x, 475, x + 70, 475, "A-GLAZ"); }
        void Door(double x, double y, double start, double end) { e.Add(new ArcEntity(new(x, y), 60, start, end) { Layer = "A-DOOR" }); var p = GeometryMath.OnCircle(new(x, y), 60, end); e.Add(new LineEntity(new(x, y), p) { Layer = "A-DOOR" }); }
        Door(290, 310, 0, 90); Door(560, 100, 0, 90); Door(90, 230, 0, 90); Door(680, 270, 0, 90);
        var desk = new BlockDefinition("Workstation", default, [PolylineEntity.FromPoints(new Vec3[] { new(0, 0), new(95, 0), new(95, 43), new(0, 43) }, true), PolylineEntity.FromPoints(new Vec3[] { new(30, 8), new(65, 8), new(65, 30), new(30, 30) }, true), new CircleEntity(new(47, -22), 15)]);
        var chair = new BlockDefinition("Chair", default, [PolylineEntity.FromPoints(new Vec3[] { new(-12, -12), new(12, -12), new(12, 12), new(-12, 12) }, true), new ArcEntity(default, 16, 15, 165)]);
        drawing = drawing with { Blocks = drawing.Blocks.Add(desk.Name, desk).Add(chair.Name, chair) };
        foreach (var p in new Vec3[] { new(330, 60), new(440, 60), new(330, 220), new(440, 220), new(330, 400), new(440, 400) }) e.Add(new BlockReferenceEntity("Workstation", p, new(1, 1, 1)) { Layer = "A-FURN" });
        Rect(55, 320, 165, 75, "A-FURN");
        foreach (var p in new Vec3[] { new(80, 300), new(135, 300), new(190, 300), new(80, 418), new(135, 418), new(190, 418) }) e.Add(new BlockReferenceEntity("Chair", p, new(1, 1, 1)) { Layer = "A-FURN" });
        Rect(625, 350, 145, 70, "A-FURN"); Rect(650, 300, 95, 30, "A-FURN"); Rect(615, 45, 175, 40, "A-FURN");
        Rect(35, 45, 215, 30, "A-FURN"); e.Add(new CircleEntity(new(230, 180), 23) { Layer = "A-FURN" });
        Text(62, 263, "MEETING ROOM", 11); Text(68, 132, "RECEPTION", 11); Text(346, 333, "DESIGN STUDIO", 13); Text(635, 441, "PROJECT ROOM", 11); Text(650, 194, "MATERIAL LIBRARY", 10);
        Text(350, 313, "06 workstations / 38.4 m²", 8); Text(79, 113, "WELCOME / 01", 8);
        e.Add(new DimensionEntity(new(0, 0), new(840, 0), new(400, -43)) { Layer = "A-DIMS" });
        e.Add(new DimensionEntity(new(0, 0), new(0, 480), new(-45, 220)) { Layer = "A-DIMS" });
        e.Add(new DimensionEntity(new(0, 480), new(290, 480), new(145, 515)) { Layer = "A-DIMS" });
        e.Add(new DimensionEntity(new(290, 480), new(560, 480), new(420, 515)) { Layer = "A-DIMS" });
        e.Add(new DimensionEntity(new(560, 480), new(840, 480), new(700, 515)) { Layer = "A-DIMS" });
        Text(0, -90, "STUDIO / GROUND FLOOR", 18); Text(0, -110, "CONCEPT DESIGN     •     ALL DIMENSIONS IN DRAWING UNITS", 8);
        Text(670, -91, "A—101", 23); Text(676, -111, "CADSPACE   /   01", 8);
        return drawing with { Entities = e.ToImmutableArray() };
    }
    public static Drawing ModelStudy()
    {
        return Drawing.Empty with { Name = "Model study.dxf", Entities = [
            MeshFactory.Box(new(-100, -70, -12), new(100, 70, 0)) with { TrueColor = 0xFF567590 },
            MeshFactory.Box(new(-85, -55, 0), new(-20, 50, 75)) with { TrueColor = 0xFF72B4D5 },
            MeshFactory.Cylinder(new(40, 0, 0), 40, 100) with { TrueColor = 0xFFE0BB78 },
            MeshFactory.Sphere(new(45, 0, 130), 30) with { TrueColor = 0xFF79BC9C },
            MeshFactory.Cone(new(-48, 0, 75), 32, 55) with { TrueColor = 0xFFB5A1D7 }
        ] };
    }
}
