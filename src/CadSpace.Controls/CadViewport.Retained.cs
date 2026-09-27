using System.Diagnostics;
using CadSpace.Rendering;
using Silk.NET.OpenGL;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Uno.WinUI.Graphics3DGL;
using Windows.Foundation;

namespace CadSpace.Controls;

public sealed partial class CadViewport
{
    private long _sceneCallbacks, _modelCallbacks;
    private RenderStamp CaptureRenderStamp(double width, double height) => new(
        _session!.Scene, _session.SelectionRevision, Camera.Center, Camera.PixelsPerUnit,
        width, height, _session.GridVisible, _session.GridSpacing, Is3D, ModelCamera.Origin,
        ModelCamera.TargetOffset, ModelCamera.Distance, ModelCamera.Yaw, ModelCamera.Pitch,
        ModelCamera.Orthographic, VisualStyle, ClippingPlane);

    private sealed class DraftSurface : SKCanvasElement
    {
        private readonly CadViewport _owner;
        private readonly SkiaDraftRenderer _renderer = new();
        private SKPicture? _picture;
        private RenderStamp? _recordedStamp;
        public DraftSurface(CadViewport owner)
        {
            _owner = owner;
            Unloaded += (_, _) => { _picture?.Dispose(); _picture = null; _recordedStamp = null; };
        }
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            _owner._sceneCallbacks++;
            var session = _owner._session; if (session == null || area.Width <= 0 || area.Height <= 0) return;
            _owner.Camera.Width = area.Width; _owner.Camera.Height = area.Height;
            if (_owner._fit && area.Width > 100 && area.Height > 100) { _owner.Camera.Fit(session.Scene.Bounds); _owner._fit = false; }
            var stamp = _owner.CaptureRenderStamp(area.Width, area.Height);
            if (_picture == null || _recordedStamp != stamp)
            {
                var start = Stopwatch.GetTimestamp();
                // A separate visual is not itself a recording cache: Uno can repaint it when
                // unrelated controls change. Retain one immutable display list per viewport.
                using var recorder = new SKPictureRecorder();
                var recording = recorder.BeginRecording(new SKRect(0, 0, (float)area.Width, (float)area.Height));
                _renderer.Render(recording, _owner.Camera, stamp.Scene, session.Selection, session.GridVisible, session.GridSpacing);
                var next = recorder.EndRecording();
                _picture?.Dispose(); _picture = next; _recordedStamp = stamp;
                _owner._sceneDraws++;
                _owner._lastCpuDrawMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            canvas.DrawPicture(_picture);
        }
    }
    private sealed class ModelSurface : GLCanvasElement
    {
        private readonly CadViewport _owner;
        private readonly GlSceneRenderer _renderer = new();
        private RenderStamp? _renderedStamp;
        private int _framebuffer;
        public ModelSurface(CadViewport owner) : base(null)
        {
            _owner = owner;
            // The host recreates its framebuffer on SizeChanged; an ID can be reused.
            SizeChanged += (_, _) => _renderedStamp = null;
        }
        protected override void Init(GL gl)
        {
            _renderedStamp = null;
            try { _renderer.Initialize(gl); _owner.DispatcherQueue.TryEnqueue(() => _owner._metrics.Text = $"GPU  •  {_renderer.Device}"); }
            catch (Exception error) { _owner.Fault($"3D renderer initialization failed: {error.Message}"); throw; }
        }
        protected override void RenderOverride(GL gl)
        {
            _owner._modelCallbacks++;
            var session = _owner._session; if (session == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            var stamp = _owner.CaptureRenderStamp(ActualWidth, ActualHeight);
            var framebuffer = gl.GetInteger(GetPName.DrawFramebufferBinding);
            // Uno 6.7.135 GLVisual schedules Render on every host repaint. Its offscreen
            // color/depth attachments persist, so unchanged callbacks need no scene draw.
            // The host still performs ReadPixels afterwards; report callbacks separately
            // and do not mistake skipped scene draws for eliminated readback/composition.
            if (_renderedStamp == stamp && _framebuffer == framebuffer) return;
            try
            {
                _renderer.Render(gl, stamp.Scene, _owner.ModelCamera, ActualWidth, ActualHeight, session.Selection, _owner.VisualStyle, _owner.ClippingPlane);
                _renderedStamp = stamp; _framebuffer = framebuffer; _owner._modelDraws++;
            }
            catch (Exception error) { _renderedStamp = null; _owner.Fault($"3D renderer error: {error.Message}"); }
        }
        protected override void OnDestroy(GL gl) { _renderedStamp = null; _renderer.Destroy(gl); }
    }
}
