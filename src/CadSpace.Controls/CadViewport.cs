using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Immutable;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Silk.NET.OpenGL;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Uno.WinUI.Graphics3DGL;
using Windows.Foundation;
using Windows.System;

namespace CadSpace.Controls;

/// <summary>Reusable two-dimensional drafting and three-dimensional GPU viewport with no application dependency.</summary>
public sealed class CadViewport : Grid
{
    private readonly DraftSurface _draft;
    private ModelSurface? _model;
    private readonly TextBlock _viewLabel = CadTheme.Text("[Top]  [2D Wireframe]", 11, 0xFFBDCDD9);
    private readonly TextBlock _metrics = CadTheme.Text("", 10, CadTheme.Muted);
    private CadSession? _session;
    private CommandEngine? _commands;
    private bool _inside, _pan, _orbit, _fit = true;
    private Vec3 _cursor, _pressWorld;
    private Point _previous, _pressScreen;
    private bool _selecting, _dragged;
    private SnapResult _snap;
    public Camera2D Camera { get; } = new();
    public Camera3D ModelCamera { get; } = new();
    public bool Is3D { get; private set; }
    public event Action<Vec3>? CoordinatesChanged;
    public event Action<string>? Message;
    public event Action<bool>? ModeChanged;
    public CadViewport()
    {
        Background = CadTheme.Brush(0xFF1D242C); _draft = new(this) { IsHitTestVisible = false }; Children.Add(_draft);
        _viewLabel.Margin = new Thickness(14, 12, 0, 0); _viewLabel.HorizontalAlignment = HorizontalAlignment.Left; _viewLabel.VerticalAlignment = VerticalAlignment.Top; _viewLabel.IsHitTestVisible = false; Children.Add(_viewLabel);
        _metrics.Margin = new Thickness(0, 0, 16, 12); _metrics.HorizontalAlignment = HorizontalAlignment.Right; _metrics.VerticalAlignment = VerticalAlignment.Bottom; _metrics.IsHitTestVisible = false; Children.Add(_metrics);
        var navigation = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 16, 0) };
        var cube = new Grid { Width = 70, Height = 64, Background = CadTheme.Brush(0xCC384451) };
        var top = CadTheme.Button("TOP", () => Set3D(false)); top.HorizontalAlignment = HorizontalAlignment.Center; top.VerticalAlignment = VerticalAlignment.Center; top.FontSize = 11; cube.Children.Add(top); navigation.Children.Add(cube);
        navigation.Children.Add(CadTheme.Button("SW ISO", () => { Set3D(true); ModelCamera.Yaw = 45; ModelCamera.Pitch = 30; Redraw(); }, 70));
        navigation.Children.Add(CadTheme.Button("FRONT", () => { Set3D(true); ModelCamera.Yaw = -90; ModelCamera.Pitch = 0; Redraw(); }, 70));
        navigation.Children.Add(CadTheme.Button("FIT", Fit, 70)); Children.Add(navigation);
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerWheelChanged += Wheel;
        PointerEntered += (_, _) => { _inside = true; Redraw(); };
        PointerExited += (_, _) => { _inside = false; Redraw(); };
        PointerCaptureLost += (_, _) => { _pan = _orbit = _selecting = false; Redraw(); };
        DoubleTapped += (_, e) => { if (_commands?.IsActive != true) { Fit(); e.Handled = true; } };
        SizeChanged += (_, _) => { Camera.Width = ActualWidth; Camera.Height = ActualHeight; Redraw(); };
    }
    public void Bind(CadSession session, CommandEngine commands)
    {
        if (_session != null) _session.Changed -= Redraw;
        if (_commands != null) { _commands.Changed -= Redraw; _commands.ViewRequested -= OnView; }
        _session = session; _commands = commands; session.Changed += Redraw; commands.Changed += Redraw; commands.ViewRequested += OnView;
        _fit = true; Set3D(false); Redraw();
    }
    public void Fit()
    {
        if (_session == null) return;
        Camera.Width = ActualWidth; Camera.Height = ActualHeight; Camera.Fit(_session.Scene.Bounds); ModelCamera.Fit(_session.Scene.Bounds); _fit = false; Redraw();
    }
    public void Set3D(bool enabled)
    {
        Is3D = enabled;
        if (enabled && _model == null)
        {
            _model = new(this) { IsHitTestVisible = false }; Children.Insert(1, _model);
            ((INotifyPropertyChanged)_model).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GLCanvasElement.IsGLInitialized) && _model.IsGLInitialized == false) Fault("The 3D GPU context could not be initialized. The 2D drafting view remains available.");
            };
        }
        _draft.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (_model != null) _model.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && _session != null) ModelCamera.Fit(_session.Scene.Bounds);
        _viewLabel.Text = enabled ? "[Custom view]  [Shaded with edges]  •  drag to orbit" : "[Top]  [2D Wireframe]";
        ModeChanged?.Invoke(enabled); Redraw();
    }
    private void OnView(string view) { if (view == "ZOOM") Fit(); else Set3D(view == "3DORBIT"); }
    public void Redraw() { if (Is3D) _model?.Invalidate(); else _draft.Invalidate(); }
    private void Fault(string message) => DispatcherQueue.TryEnqueue(() => { Message?.Invoke(message); Set3D(false); });
    private Vec3 World(Point screen) => Camera.ScreenToWorld(screen.X, screen.Y);
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session == null || _commands == null) return;
        var current = e.GetCurrentPoint(this); _previous = _pressScreen = current.Position; _pressWorld = World(current.Position); _dragged = false;
        if (current.Properties.IsRightButtonPressed) { _commands.Submit(""); e.Handled = true; return; }
        _pan = current.Properties.IsMiddleButtonPressed; _orbit = Is3D && current.Properties.IsLeftButtonPressed;
        if (_pan || _orbit) { CapturePointer(e.Pointer); e.Handled = true; return; }
        if (!current.Properties.IsLeftButtonPressed) return;
        if (_commands.IsActive)
        {
            _snap = _session.Snap(_pressWorld, 9 / Camera.PixelsPerUnit, _commands.ReferencePoint); _commands.Point(_snap.Point);
        }
        else { _selecting = true; CapturePointer(e.Pointer); }
        Redraw(); e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_session == null || _commands == null) return;
        var current = e.GetCurrentPoint(this); var p = current.Position;
        var dx = p.X - _previous.X; var dy = p.Y - _previous.Y; _previous = p;
        if (_pan) { if (Is3D) ModelCamera.Pan(dx, dy, ActualHeight); else Camera.Pan(dx, dy); }
        if (_orbit) ModelCamera.Orbit(dx, dy);
        if (_selecting && Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 5) _dragged = true;
        var world = World(p); _snap = _commands.IsActive ? _session.Snap(world, 9 / Camera.PixelsPerUnit, _commands.ReferencePoint) : new(world, SnapKind.None);
        _cursor = _snap.Point; CoordinatesChanged?.Invoke(_cursor); Redraw();
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_session != null && _selecting)
        {
            var point = e.GetCurrentPoint(this).Position; var world = World(point); var additive = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
            if (_dragged) _session.SelectWindow(_pressWorld, world, point.X < _pressScreen.X, additive);
            else _session.Select(_session.HitTest(world, 6 / Camera.PixelsPerUnit), additive);
        }
        _pan = _orbit = _selecting = _dragged = false; ReleasePointerCapture(e.Pointer); Redraw(); e.Handled = true;
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this); var factor = Math.Pow(1.18, p.Properties.MouseWheelDelta / 120.0);
        if (Is3D) ModelCamera.Zoom(factor); else Camera.Zoom(factor, p.Position.X, p.Position.Y);
        Redraw(); e.Handled = true;
    }
    private sealed class DraftSurface(CadViewport owner) : SKCanvasElement
    {
        private readonly SkiaDraftRenderer _renderer = new();
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var session = owner._session; if (session == null) return;
            owner.Camera.Width = area.Width; owner.Camera.Height = area.Height;
            if (owner._fit && area.Width > 100 && area.Height > 100) { owner.Camera.Fit(session.Scene.Bounds); owner._fit = false; }
            var start = Stopwatch.GetTimestamp(); _renderer.Render(canvas, owner.Camera, session.Scene, session.Selection, session.GridVisible, session.GridSpacing);
            if (owner._commands?.IsActive == true)
            {
                try
                {
                    var preview = owner._commands.Preview(owner._cursor);
                    if (preview.Count > 0) _renderer.DrawScene(canvas, owner.Camera, EntityGeometry.BuildScene(session.Document.Drawing with { Entities = preview.ToImmutableArray() }), new HashSet<Guid>(), true);
                }
                catch (NotSupportedException) { /* A preview must never invalidate a valid drawing. */ }
            }
            _renderer.DrawInteraction(canvas, owner.Camera, owner._cursor, owner._inside, owner._snap.Kind is SnapKind.None or SnapKind.Grid ? null : owner._snap.Kind.ToString(), owner._selecting && owner._dragged ? owner._pressWorld : null);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            // CPU draw-recording time, deliberately not labeled GPU time or FPS.
            owner.DispatcherQueue.TryEnqueue(() => owner._metrics.Text = $"{session.Document.Drawing.Entities.Length} objects   •   CPU draw {elapsed:0.0} ms");
        }
    }
    private sealed class ModelSurface(CadViewport owner) : GLCanvasElement(null)
    {
        private readonly GlSceneRenderer _renderer = new();
        protected override void Init(GL gl)
        {
            try { _renderer.Initialize(gl); owner.DispatcherQueue.TryEnqueue(() => owner._metrics.Text = $"GPU  •  {_renderer.Device}"); }
            catch (Exception error) { owner.Fault($"3D renderer initialization failed: {error.Message}"); throw; }
        }
        protected override void RenderOverride(GL gl)
        {
            if (owner._session == null) return;
            try { _renderer.Render(gl, owner._session.Scene, owner.ModelCamera, ActualWidth, ActualHeight); }
            catch (Exception error) { owner.Fault($"3D renderer error: {error.Message}"); }
        }
        protected override void OnDestroy(GL gl) => _renderer.Destroy(gl);
    }
}
