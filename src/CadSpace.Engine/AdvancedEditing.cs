using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public static class AdvancedEditing
{
    public static ImmutableArray<Vec3> Profile(Entity entity)
    {
        if(entity is PlacedEntity placed) return Profile(placed.Geometry).Select(placed.Placement.Point).ToImmutableArray();
        return entity switch
        {
            PolylineEntity {Closed:true} p => EntityGeometry.PolylinePoints(p),
            Polyline3DEntity {Closed:true} p => p.Points,
            CircleEntity c => EntityGeometry.Curve(c.Center,c.Radius,0,360).SkipLast(1).ToImmutableArray(),
            _ => throw new ArgumentException("Select closed planar polylines or circles with matching sampled vertex counts.")
        };
    }
    public static void LoftSelection(this CadSession session)
    {
        var selected=session.EditableSelection();
        var mesh=MeshSurfaces.Loft(selected.Select(Profile).ToArray());
        session.Add("Loft",mesh with{TrueColor=selected[0].TrueColor});
    }
    public static void SweepSelection(this CadSession session)
    {
        var selected=session.EditableSelection();
        if(selected.Length!=2) throw new ArgumentException("Select one closed profile and one open polyline path.");
        var path=selected.FirstOrDefault(e=>e is Polyline3DEntity {Closed:false} or PolylineEntity {Closed:false});
        var points=path switch {Polyline3DEntity p=>p.Points,PolylineEntity p=>EntityGeometry.PolylinePoints(p),_=>throw new ArgumentException("An open polyline path is required.")};
        var profile=selected.First(e=>e!=path);
        session.Add("Sweep",MeshSurfaces.Sweep(Profile(profile),points) with{TrueColor=profile.TrueColor});
    }
    public static SplineEntity ControlSpline(IEnumerable<Vec3> controls)
    {
        var points=controls.ToImmutableArray();
        if(points.Length<2) throw new ArgumentException("At least two spline control points are required.");
        var degree=Math.Min(3,points.Length-1);var knots=ImmutableArray.CreateBuilder<double>();
        for(var i=0;i<points.Length+degree+1;i++) knots.Add(i<=degree?0:i>=points.Length?1:(double)(i-degree)/(points.Length-degree));
        return new(degree,points,knots.ToImmutable(),[]);
    }
    public static Transform3 MirrorPlane(Vec3 a,Vec3 b,Vec3 c)
    {
        var n=(b-a).Cross(c-a).Normalized;
        Vec3 Reflect(Vec3 v)=>v-n*(2*v.Dot(n));
        return new Transform3(Reflect(Vec3.UnitX),Reflect(Vec3.UnitY),Reflect(Vec3.UnitZ),default).About(a);
    }
    public static Transform3 Align(Vec3 a,Vec3 b,Vec3 c,Vec3 x,Vec3 y,Vec3 z)
    {
        var source=Coordinates3D.Frame(a,b,c);var target=Coordinates3D.Frame(x,y,z);
        return Coordinates3D.Inverse(source).Then(target);
    }
    /// <summary>Exact circular arc for one XY polyline bulge, preserving direction as an unoriented DXF arc.</summary>
    public static Entity ExplodeSegment(PolyVertex a,PolyVertex b)
    {
        if(Math.Abs(a.Bulge)<1e-12) return new LineEntity(a.Position,b.Position);
        var chord=b.Position-a.Position;
        if(Math.Abs(chord.Z)>1e-8) throw new ArgumentException("Bulged segments require constant elevation.");
        var length=chord.Length; var normal=new Vec3(-chord.Y,chord.X).Normalized;
        var center=(a.Position+b.Position)/2+normal*(length*(1-a.Bulge*a.Bulge)/(4*a.Bulge));
        var first=GeometryMath.Angle(a.Position-center); var second=GeometryMath.Angle(b.Position-center);
        return new ArcEntity(center,center.DistanceTo(a.Position),a.Bulge>0?first:second,a.Bulge>0?second:first);
    }
}
