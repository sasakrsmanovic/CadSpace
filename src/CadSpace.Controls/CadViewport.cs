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
    private readonly CadDynamicInput _dynamic=new();
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
    private double _lastCpuDrawMilliseconds;
    private long _lastMetricUpdate;
    public Camera2D Camera { get; } = new();
    public Camera3D ModelCamera { get; } = new();
    public bool Is3D { get; private set; }
    public ModelVisualStyle VisualStyle { get; set; } = ModelVisualStyle.ShadedEdges;
    public Plane3? ClippingPlane { get; set; }
    private readonly ComboBox _styleSelector = new() { ItemsSource = Enum.GetNames<ModelVisualStyle>(), SelectedItem = nameof(ModelVisualStyle.ShadedEdges), Width = 126, MinHeight = 26, FontSize = 10 };
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
        navigation.Children.Add(CadTheme.Button("FIT", Fit, 70));
        navigation.Children.Add(_styleSelector);
        _styleSelector.SelectionChanged += (_, _) => { if (_styleSelector.SelectedItem is string style && Enum.TryParse<ModelVisualStyle>(style, out var value)) { VisualStyle = value; Set3D(true); UpdateViewLabel(); Redraw(); } };
        navigation.Children.Add(CadTheme.Button("Perspective / Ortho", () => { ModelCamera.Orthographic = !ModelCamera.Orthographic; Set3D(true); UpdateViewLabel(); Redraw(); }, 126));
        navigation.Children.Add(CadTheme.Button("Clip half / Off", () => { ClippingPlane = ClippingPlane == null ? Plane3.Through(_session?.Scene.Bounds.Center ?? default, Vec3.UnitZ) : null; Set3D(true); UpdateViewLabel(); Redraw(); }, 126));
        Children.Add(navigation); Children.Add(_dynamic);
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
        _dynamic.Bind(commands); _fit = true; ClippingPlane = null; Set3D(false); Redraw();
    }
    public void Fit()
    {
        if (_session == null) return;
        Camera.Width = ActualWidth; Camera.Height = ActualHeight; Camera.Fit(_session.Scene.Bounds); ModelCamera.Fit(_session.Scene.Bounds); _fit = false; Redraw();
    }
    public void Set3D(bool enabled)
    {
        var changed = Is3D != enabled;
        Is3D = enabled;
        if (enabled && _model == null)
        {
            _model = new(this) { IsHitTestVisible = false }; Children.Insert(1, _model);
            _model.RegisterPropertyChangedCallback(GLCanvasElement.IsGLInitializedProperty, (_, _) =>
            {
                if (_model.IsGLInitialized == false) Fault("The 3D GPU context could not be initialized. The 2D drafting view remains available.");
            });
        }
        _draft.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (_model != null) _model.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && changed && _session != null) ModelCamera.Fit(_session.Scene.Bounds);
        UpdateViewLabel();
        ModeChanged?.Invoke(enabled); Redraw();
    }
    private void UpdateViewLabel() => _viewLabel.Text = Is3D ? $"[{(ModelCamera.Orthographic ? "Orthographic" : "Perspective")}]  [{VisualStyle}]  •  click to select, drag to orbit" + (ClippingPlane != null ? "  •  section clipping (uncapped)" : "") : "[Top]  [2D Wireframe]";
    private void OnView(string view)
    {
        if (view == "ZOOM") { Fit(); return; }
        if (view.StartsWith("STYLE:")) { VisualStyle = Enum.Parse<ModelVisualStyle>(view[6..], true); _styleSelector.SelectedItem = VisualStyle.ToString(); Set3D(true); }
        else if (view.StartsWith("PROJECTION:")) { ModelCamera.Orthographic = view[11..] == "0"; Set3D(true); }
        else if (view.StartsWith("CLIP:"))
        {
            if (view[5..] == "OFF") ClippingPlane = null;
            else { var values = view[5..].Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); ClippingPlane = Plane3.Through(new(values[0], values[1], values[2]), new(values[3], values[4], values[5])); }
            Set3D(true);
        }
        else Set3D(view == "3DORBIT");
        UpdateViewLabel(); Redraw(); Console.WriteLine($"CADSPACE_VIEW: {view}");
    }
    private bool _redrawPending;
    public void Redraw()
    {
        if(_redrawPending)return;_redrawPending=true;
        if(!DispatcherQueue.TryEnqueue(()=>{_redrawPending=false;DrawPending();}))_redrawPending=false;
    }
    private void DrawPending()
    {
        _dynamic.Refresh();
        if (Is3D) _model?.Invalidate();
        else
        {
            if (_session != null && Stopwatch.GetElapsedTime(_lastMetricUpdate).TotalMilliseconds>250) { _lastMetricUpdate=Stopwatch.GetTimestamp(); _metrics.Text = $"{_session.Document.Drawing.Entities.Length} objects   •   previous CPU draw {_lastCpuDrawMilliseconds:0.0} ms"; }
            _draft.Invalidate();
        }
    }
    private void Fault(string message) => DispatcherQueue.TryEnqueue(() => { Message?.Invoke(message); Set3D(false); });
    private Vec3 World(Point screen)
    {
        if (!Is3D) return Camera.ScreenToWorld(screen.X, screen.Y);
        return ModelCamera.Ray(screen.X, screen.Y, ActualWidth, ActualHeight).IntersectPlane(new(0, 0, _commands?.ReferencePoint?.Z ?? 0), Vec3.UnitZ, out var p) ? p : _cursor;
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session == null || _commands == null) return;
        var current = e.GetCurrentPoint(this); _previous = _pressScreen = current.Position; _pressWorld = World(current.Position); _dragged = false;
        if (current.Properties.IsRightButtonPressed)
        {
            if(_commands.IsActive) _commands.Submit("");
            else
            {
                var menu=new MenuFlyout();
                foreach(var name in new[]{"MOVE","COPY","ERASE","SELECTALL","ZOOM","UNDO","REDO"})
                {var item=new MenuFlyoutItem{Text=name};item.Click+=(_,_)=>_commands.Start(name);menu.Items.Add(item);}
                menu.ShowAt(this,new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions{Position=current.Position});
            }
            e.Handled=true;return;
        }
        _pan = current.Properties.IsMiddleButtonPressed; _orbit = Is3D && !_commands.IsActive && current.Properties.IsLeftButtonPressed;
        if (_pan || _orbit) { CapturePointer(e.Pointer); e.Handled = true; return; }
        if (!current.Properties.IsLeftButtonPressed) return;
        if (_commands.IsActive)
        {
            _snap = _session.Snap(_pressWorld, 9 / Camera.PixelsPerUnit, _commands.ReferencePoint); _cursor = _snap.Point; _commands.PickTolerance = 7 / Camera.PixelsPerUnit; _commands.Point(_snap.Point);
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
        if (_orbit && (Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 5 || _dragged)) { _dragged = true; ModelCamera.Orbit(dx, dy); }
        if (_selecting && Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 5) _dragged = true;
        var world = World(p); _snap = _commands.IsActive ? _session.Snap(world, 9 / Camera.PixelsPerUnit, _commands.ReferencePoint) : new(world, SnapKind.None);
        _cursor = _snap.Point; _dynamic.Position(p.X,p.Y,ActualWidth,ActualHeight,_cursor); CoordinatesChanged?.Invoke(_cursor); Redraw();
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_session != null && _orbit && !_dragged)
        {
            var point = e.GetCurrentPoint(this).Position;
            var pick = ScenePicking.Pick(_session.Scene, ModelCamera.Ray(point.X, point.Y, ActualWidth, ActualHeight), p => ModelCamera.Project(p, ActualWidth, ActualHeight), new(point.X, point.Y), 7, ClippingPlane, VisualStyle == ModelVisualStyle.Wireframe);
            _session.Select(pick?.EntityId, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control));
            Console.WriteLine($"CADSPACE_PICK: count={_session.Selection.Count}");
        }
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
        if (Is3D) ModelCamera.ZoomAt(factor, p.Position.X, p.Position.Y, ActualWidth, ActualHeight); else Camera.Zoom(factor, p.Position.X, p.Position.Y);
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
                    if (preview.Count > 0) _renderer.DrawScene(canvas, owner.Camera, EntityGeometry.BuildScene(session.Document.Drawing with { Entities = preview.Select(e => e with { Layout = session.ActiveLayout }).ToImmutableArray() }, session.ActiveLayout), new HashSet<Guid>(), true);
                }
                catch (NotSupportedException) { /* A preview must never invalidate a valid drawing. */ }
            }
            _renderer.DrawInteraction(canvas, owner.Camera, owner._cursor, owner._inside, owner._snap.Kind is SnapKind.None or SnapKind.Grid ? null : owner._snap.Kind.ToString(), owner._selecting && owner._dragged ? owner._pressWorld : null);
            owner._lastCpuDrawMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
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
            try { _renderer.Render(gl, owner._session.Scene, owner.ModelCamera, ActualWidth, ActualHeight, owner._session.Selection, owner.VisualStyle, owner.ClippingPlane); }
            catch (Exception error) { owner.Fault($"3D renderer error: {error.Message}"); }
        }
        protected override void OnDestroy(GL gl) => _renderer.Destroy(gl);
    }
}
