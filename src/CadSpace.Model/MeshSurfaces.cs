using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Capped polygonal loft and parallel-transport sweep. Explicit triangle meshes, not analytic B-rep surfaces.</summary>
public static class MeshSurfaces
{
    public static MeshEntity Loft(IReadOnlyList<ImmutableArray<Vec3>> profiles)
    {
        if (profiles.Count < 2 || profiles.Count > 4096) throw new ArgumentException("Loft needs 2–4096 profiles.");
        var count = profiles[0].Length;
        if (count < 3 || count > 2048 || (long)count * count * profiles.Count > 8000000 || (long)count * profiles.Count > 100000 || profiles.Any(p => p.Length != count || p.Any(v => !v.IsFinite)))
            throw new ArgumentException("Loft profiles need matching vertex counts and at most 100,000 total vertices.");
        foreach (var p in profiles) _ = Triangulation.Polygon3D(p);
        var vertices = profiles.SelectMany(p => p).ToImmutableArray();
        var triangles = ImmutableArray.CreateBuilder<int>();
        for (var ring=0;ring+1<profiles.Count;ring++) for(var j=0;j<count;j++)
        {
            var a=ring*count+j;var b=ring*count+(j+1)%count;var c=b+count;var d=a+count;
            triangles.AddRange(new[]{a,b,c,a,c,d});
        }
        var first=Triangulation.Polygon3D(profiles[0]); var last=Triangulation.Polygon3D(profiles[^1]);
        for(var i=0;i<first.Length;i+=3) triangles.AddRange(new[]{first[i+2],first[i+1],first[i]});
        triangles.AddRange(last.Select(i=>i+(profiles.Count-1)*count));
        var mesh=new MeshEntity(vertices,triangles.ToImmutable(),"Polygonal loft");
        if(MeshFactory.SignedVolume(mesh)<0) mesh=mesh with {Triangles=mesh.Triangles.Chunk(3).SelectMany(t=>new[]{t[0],t[2],t[1]}).ToImmutableArray()};
        MeshBoolean.ValidateClosed(mesh);
        return mesh;
    }
    public static MeshEntity Sweep(ImmutableArray<Vec3> profile, ImmutableArray<Vec3> path)
    {
        if(path.Length<2 || path.Length>4096 || path.Any(p=>!p.IsFinite)) throw new ArgumentException("Sweep needs a finite open path of 2–4096 vertices.");
        if(profile.Length<3 || profile.Length>2048)throw new ArgumentException("Sweep profile needs 3–2048 vertices.");
        _ = Triangulation.Polygon3D(profile);
        if((long)profile.Length*path.Length>100000) throw new ArgumentException("Sweep exceeds 100,000 vertices.");
        var center=profile.Aggregate(Vec3.Zero,(s,p)=>s+p)/profile.Length;
        var normal=Vec3.Zero; for(var i=1;i+1<profile.Length;i++) normal+=(profile[i]-profile[0]).Cross(profile[i+1]-profile[0]);normal=normal.Normalized;
        var axis=(profile[1]-profile[0]).Normalized;var up=normal.Cross(axis);
        var local=profile.Select(p=>new Vec3((p-center).Dot(axis),(p-center).Dot(up))).ToArray();
        var directions=new Vec3[path.Length-1];
        for(var i=0;i<directions.Length;i++) directions[i]=(path[i+1]-path[i]).Normalized;
        var tangents=new Vec3[path.Length]; tangents[0]=directions[0]; tangents[^1]=directions[^1];
        for(var i=1;i+1<path.Length;i++)
        {
            if(directions[i-1].Dot(directions[i])<-.999) throw new ArgumentException("Sweep path reverses direction at a vertex.");
            tangents[i]=(directions[i-1]+directions[i]).Normalized;
        }
        var rings=new List<ImmutableArray<Vec3>>(); var x=Coordinates3D.ObjectCoordinateSystem(tangents[0]).X;
        for(var i=0;i<path.Length;i++)
        {
            if(i>0)
            {
                var rotationAxis=tangents[i-1].Cross(tangents[i]);var sine=rotationAxis.Length;
                if(sine>1e-12) x=Transform3.RotationAxis(rotationAxis,GeometryMath.Degrees(Math.Atan2(sine,tangents[i-1].Dot(tangents[i])))).Vector(x);
                x=(x-tangents[i]*x.Dot(tangents[i])).Normalized;
            }
            var y=tangents[i].Cross(x);
            rings.Add(local.Select(p=>path[i]+x*p.X+y*p.Y).ToImmutableArray());
        }
        return Loft(rings) with {Operation="Parallel-transport sweep"};
    }
}
