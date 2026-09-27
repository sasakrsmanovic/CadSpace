using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Rendering;

internal static class ViewingRegression
{
    public static void Register(Action<string, Action> test, Action<bool, string> check, Action<double, double, double> near)
    {
        void Check(bool c) => check(c, "Assertion failed");
        void Near(double a, double b, double epsilon = 1e-7) => near(a, b, epsilon);
        Camera3D Camera(bool ortho = false) => new() { Origin = new(1e8, -1e8, 3e7), TargetOffset = new(30, 40, 50), Distance = 1000, Yaw = 27, Pitch = 34, Orthographic = ortho };
        foreach (var orthographic in new[] { false, true })
        {
            test($"camera ray and projection roundtrip ({orthographic})", () =>
            {
                var c = Camera(orthographic);
                foreach (var p in new Vec3[] { new(50, 50), new(800, 500), new(1300, 700) })
                {
                    var ray = c.Ray(p.X, p.Y, 1600, 1000); var point = ray.At(1500); var screen = c.Project(point, 1600, 1000);
                    Near(screen.X, p.X, 1e-6); Near(screen.Y, p.Y, 1e-6);
                }
            });
            test($"pointer anchored 3D zoom ({orthographic})", () =>
            {
                var c = Camera(orthographic); var target = c.Target; Check(c.Ray(400, 300, 1600, 1000).IntersectPlane(target, c.Backward, out var anchor));
                c.ZoomAt(1.4, 400, 300, 1600, 1000); var p = c.Project(anchor, 1600, 1000); Near(p.X, 400, 1e-6); Near(p.Y, 300, 1e-6);
            });
            test($"GPU projection matches double camera ({orthographic})", () =>
            {
                var c = Camera(orthographic); var point = c.Target + new Vec3(31, 62, -85); var rebased = point - c.Origin;
                var v = System.Numerics.Vector4.Transform(new System.Numerics.Vector4((float)rebased.X, (float)rebased.Y, (float)rebased.Z, 1), c.Matrix(1.6)); var screen = c.Project(point,1600,1000);
                Near((v.X/v.W+1)*800, screen.X,.001); Near((1-v.Y/v.W)*500,screen.Y,.001);
            });
        }
        test("3D pick selects frontmost triangle at survey coordinates", () =>
        {
            var c=Camera(); var back=MeshFactory.Box(c.Target-new Vec3(50,50,50),c.Target+new Vec3(50,50,50)); var front=MeshFactory.Box(c.Target+c.Backward*200-new Vec3(30,30,30),c.Target+c.Backward*200+new Vec3(30,30,30));
            var scene=EntityGeometry.BuildScene(Drawing.Empty with{Entities=[back,front]}); var hit=ScenePicking.Pick(scene,c.Ray(800,500,1600,1000),p=>c.Project(p,1600,1000),new(800,500)); Check(hit?.EntityId==front.Id);
        });
        test("3D clip rejects removed front surface and selects retained object", () =>
        {
            var front=MeshFactory.Box(new(-10,-10,10),new(10,10,20)); var back=MeshFactory.Box(new(-10,-10,-20),new(10,10,-10));
            var scene=EntityGeometry.BuildScene(Drawing.Empty with{Entities=[front,back]}); var ray=new Ray3(new(0,0,100),-Vec3.UnitZ);
            var hit=ScenePicking.Pick(scene,ray,p=>new(p.X,p.Y,100-p.Z),default,1,Plane3.Through(default,Vec3.UnitZ)); Check(hit?.EntityId==back.Id);
        });
        test("wireframe picking does not treat empty face area as selectable", () =>
        {
            var mesh=MeshFactory.Box(new(-20,-20,-10),new(20,20,10)); var scene=EntityGeometry.BuildScene(Drawing.Empty with{Entities=[mesh]});
            Check(ScenePicking.Pick(scene,new(new(0,0,100),-Vec3.UnitZ),p=>new(p.X,p.Y,100-p.Z),default,1,wireframe:true)==null);
        });
        test("point entity is pickable in 3D with pixel tolerance", () =>
        {
            var p=new PointEntity(new(2,3)); var scene=EntityGeometry.BuildScene(Drawing.Empty with{Entities=[p]});
            Check(ScenePicking.Pick(scene,new(new(2,3,100),-Vec3.UnitZ),v=>new(v.X,v.Y,100-v.Z),new(2.2,3.1),1)?.EntityId==p.Id);
        });
        test("layout switching isolates selection and new geometry", () =>
        {
            var s=new CadSession(); s.Add("model",new LineEntity(default,new(10,0))); s.SelectAll(); Check(s.Selection.Count==1);
            s.ActiveLayout="Sheet A"; Check(s.Selection.Count==0 && s.Scene.Paths.Length==0); s.Add("paper",new CircleEntity(new(2,3),5)); s.SelectAll(); Check(s.Selection.Count==1);
            Check(s.Document.Drawing.Entities[1].Layout=="Sheet A"); s.ActiveLayout="Model"; Check(s.Scene.Paths.Length==1 && s.Selection.Count==0);
        });
        test("hidden geometry cannot be selected by SelectAll", () =>
        {
            var s=new CadSession(new(Drawing.Empty with{Entities=[new LineEntity(default,new(10,10)){Visible=false}]}));s.SelectAll();Check(s.Selection.Count==0 && s.Scene.Paths.Length==0);
        });
        test("view commands validate values without document edits", () =>
        {
            var s=new CadSession();var c=new CommandEngine(s);var requests=new List<string>();c.ViewRequested+=requests.Add;
            foreach(var v in new[]{"VS","HiddenLine","PERSPECTIVE","0","CLIP3D","0,0,5,0,0,1","CLIP3D","OFF"})c.Submit(v);
            Check(requests.SequenceEqual(new[]{"STYLE:HiddenLine","PROJECTION:0","CLIP:0,0,5,0,0,1","CLIP:OFF"}) && !s.Document.CanUndo);
        });
        test("invalid clip normal makes no view change", () => { var c=new CommandEngine(new());var changed=false;c.ViewRequested+=_=>changed=true;c.Start("CLIP3D");c.Submit("0,0,0,0,0,0");Check(!changed); });
    }
}
