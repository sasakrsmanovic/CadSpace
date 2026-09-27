using System.Numerics;
using CadSpace.Geometry;
using CadSpace.Model;
using Silk.NET.OpenGL;

namespace CadSpace.Rendering;

/// <summary>Depth-tested GPU triangle/line renderer. Uses the common OpenGL 3.3 / GLES 3.0 subset.</summary>
public sealed class GlSceneRenderer
{
    private uint _program, _vao, _vertices;
    private int _matrixLocation, _triangleCount, _lineCount;
    private DrawingScene? _uploaded;
    private Vec3 _origin;
    public string Device { get; private set; } = "Not initialized";
    public unsafe void Initialize(GL gl)
    {
        var language = gl.GetStringS(StringName.ShadingLanguageVersion);
        var version = language.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase) ? "#version 300 es" : "#version 330 core";
        var vertex = Compile(gl, ShaderType.VertexShader, version + "\nprecision highp float;\nlayout(location=0) in vec3 aPosition;\nlayout(location=1) in vec3 aNormal;\nlayout(location=2) in vec3 aColor;\nuniform mat4 uMatrix;\nout vec3 vColor;\nvoid main(){ float n=length(aNormal); float light=n<0.1?1.0:(0.35+0.65*abs(dot(normalize(aNormal),normalize(vec3(0.4,-0.5,0.8)))));vColor=aColor*light;gl_Position=uMatrix*vec4(aPosition,1.0);}\n");
        var fragment = Compile(gl, ShaderType.FragmentShader, version + "\nprecision highp float;\nin vec3 vColor;\nout vec4 outColor;\nvoid main(){outColor=vec4(vColor,1.0);}\n");
        _program = gl.CreateProgram();
        try
        {
            gl.AttachShader(_program, vertex); gl.AttachShader(_program, fragment); gl.LinkProgram(_program);
            gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var success);
            if (success == 0) throw new InvalidOperationException(gl.GetProgramInfoLog(_program));
        }
        finally { gl.DeleteShader(vertex); gl.DeleteShader(fragment); }
        _matrixLocation = gl.GetUniformLocation(_program, "uMatrix");
        var oldVao = gl.GetInteger(GetPName.VertexArrayBinding); var oldBuffer = gl.GetInteger(GetPName.ArrayBufferBinding);
        _vao = gl.GenVertexArray(); _vertices = gl.GenBuffer(); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
        for (uint i = 0; i < 3; i++) { gl.VertexAttribPointer(i, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(i * 3 * sizeof(float))); gl.EnableVertexAttribArray(i); }
        gl.BindVertexArray((uint)oldVao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)oldBuffer);
        Device = gl.GetStringS(StringName.Renderer); _uploaded = null;
    }
    private static uint Compile(GL gl, ShaderType type, string source)
    {
        var shader = gl.CreateShader(type); gl.ShaderSource(shader, source); gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var success);
        if (success == 0) { var error = gl.GetShaderInfoLog(shader); gl.DeleteShader(shader); throw new InvalidOperationException(error); }
        return shader;
    }
    public unsafe void Render(GL gl, DrawingScene scene, Camera3D camera, double width, double height)
    {
        var oldProgram = gl.GetInteger(GetPName.CurrentProgram); var oldVao = gl.GetInteger(GetPName.VertexArrayBinding); var oldBuffer = gl.GetInteger(GetPName.ArrayBufferBinding);
        var depth = gl.IsEnabled(EnableCap.DepthTest); var blend = gl.IsEnabled(EnableCap.Blend); var cull = gl.IsEnabled(EnableCap.CullFace);
        var oldDepthFunction = gl.GetInteger(GetPName.DepthFunc); var oldDepthMask = gl.GetInteger(GetPName.DepthWritemask);
        try
        {
            gl.Enable(EnableCap.DepthTest); gl.DepthFunc(DepthFunction.Lequal); gl.DepthMask(true); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.CullFace);
            gl.ClearColor(0.114f, 0.141f, 0.173f, 1); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            gl.UseProgram(_program); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
            if (!ReferenceEquals(scene, _uploaded) || _origin != camera.Origin) Upload(gl, scene, camera.Origin);
            var matrix = camera.Matrix(width / Math.Max(1, height)); gl.UniformMatrix4(_matrixLocation, 1, false, (float*)&matrix);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_triangleCount);
            gl.DrawArrays(PrimitiveType.Lines, _triangleCount, (uint)_lineCount);
        }
        finally
        {
            gl.UseProgram((uint)oldProgram); gl.BindVertexArray((uint)oldVao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)oldBuffer);
            if (!depth) gl.Disable(EnableCap.DepthTest); if (blend) gl.Enable(EnableCap.Blend); if (cull) gl.Enable(EnableCap.CullFace);
            gl.DepthFunc((DepthFunction)oldDepthFunction); gl.DepthMask(oldDepthMask != 0);
        }
    }
    private void Upload(GL gl, DrawingScene scene, Vec3 origin)
    {
        var data = new List<float>();
        void Vertex(Vec3 position, Vec3 normal, uint color, double tint = 1)
        {
            var p = position - origin;
            data.AddRange([(float)p.X, (float)p.Y, (float)p.Z, (float)normal.X, (float)normal.Y, (float)normal.Z, (float)(((color >> 16) & 255) / 255.0 * tint), (float)(((color >> 8) & 255) / 255.0 * tint), (float)((color & 255) / 255.0 * tint)]);
        }
        foreach (var triangle in scene.Triangles)
        {
            var n = (triangle.B - triangle.A).Cross(triangle.C - triangle.A); if (n.Length < 1e-12) continue; n = n.Normalized;
            Vertex(triangle.A, n, triangle.Color); Vertex(triangle.B, n, triangle.Color); Vertex(triangle.C, n, triangle.Color);
        }
        _triangleCount = data.Count / 9;
        foreach (var path in scene.Paths)
            for (var i = 0; i < path.Points.Length - (path.Closed ? 0 : 1); i++) { Vertex(path.Points[i], default, path.Color, 0.8); Vertex(path.Points[(i + 1) % path.Points.Length], default, path.Color, 0.8); }
        _lineCount = data.Count / 9 - _triangleCount;
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data.ToArray().AsSpan(), BufferUsageARB.StaticDraw);
        _origin = origin; _uploaded = scene;
    }
    public void Destroy(GL gl)
    {
        if (_vao != 0) gl.DeleteVertexArray(_vao); if (_vertices != 0) gl.DeleteBuffer(_vertices); if (_program != 0) gl.DeleteProgram(_program);
        _vao = _vertices = _program = 0; _uploaded = null;
    }
}
