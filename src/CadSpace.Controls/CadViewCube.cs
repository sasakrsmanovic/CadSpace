using CadSpace.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace CadSpace.Controls;

/// <summary>Camera-synchronized cube with face/edge/corner picking, orbit drag and keyboard-accessible standard views.</summary>
public sealed class CadViewCube : Grid
{
    private readonly Surface _surface;
    private double _yaw = -90, _pitch = 90;
    public event Action<ViewOrientation>? OrientationRequested;
    public event Action<double, double>? OrbitRequested;
    public event Action<string>? NavigationRequested;
    public CadViewCube()
    {
        Width = 128; Height = 151; RowDefinitions.Add(new() { Height = new GridLength(128) }); RowDefinitions.Add(new() { Height = new GridLength(23) });
        _surface = new(this); Children.Add(_surface);
        CadUi.Identify(_surface, "viewport.cube", "ViewCube: click face, edge or corner; drag to orbit");
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        bottom.Children.Add(CadUi.IconButton("HOME", "Home view / fit", () => NavigationRequested?.Invoke("home"), "viewport.home", 23));
        var views = CadUi.TextButton("WCS ▾", () => { }, "viewport.standardViews"); var menu = new MenuFlyout();
        foreach (var name in ViewCubeGeometry.Names) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => OrientationRequested?.Invoke(ViewCubeGeometry.Named(name)); menu.Items.Add(item); }
        views.Flyout = menu; bottom.Children.Add(views); Grid.SetRow(bottom, 1); Children.Add(bottom);
        Point start = default, last = default; bool pressed = false, moved = false;
        _surface.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return; start = last = e.GetCurrentPoint(_surface).Position; pressed = true; moved = false; _surface.CapturePointer(e.Pointer); e.Handled = true; };
        _surface.PointerMoved += (_, e) => { if (!pressed) return; var p = e.GetCurrentPoint(_surface).Position; if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 5 || moved) { moved = true; OrbitRequested?.Invoke(p.X - last.X, p.Y - last.Y); } last = p; e.Handled = true; };
        _surface.PointerReleased += (_, e) => {
            if (!pressed) return; var p = e.GetCurrentPoint(_surface).Position; pressed = false; _surface.ReleasePointerCapture(e.Pointer);
            if (!moved && ViewCubeGeometry.Pick(ViewCubeGeometry.Faces(_yaw, _pitch), p.X, p.Y) is { } orientation) OrientationRequested?.Invoke(orientation); e.Handled = true;
        };
        _surface.PointerCaptureLost += (_, _) => pressed = false;
    }
    public void Synchronize(double yaw, double pitch)
    { if (_yaw == yaw && _pitch == pitch) return; _yaw = yaw; _pitch = pitch; _surface.Invalidate(); }
    private sealed class Surface(CadViewCube owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            canvas.Save(); canvas.Scale((float)(area.Width / 128), (float)(area.Height / 128));
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = new SKColor(106, 123, 142, 150) };
            using var font = new SKFont(SKTypeface.Default, 10);
            canvas.DrawCircle(64, 64, 53, paint); paint.Style = SKPaintStyle.Fill; paint.Color = new SKColor(179, 192, 206);
            foreach (var (text, x, y) in new[] { ("N", 61f, 8f), ("S", 61f, 126f), ("W", 0f, 68f), ("E", 120f, 68f) }) canvas.DrawText(text, x, y, font, paint);
            foreach (var face in ViewCubeGeometry.Faces(owner._yaw, owner._pitch))
            {
                using var path = new SKPath(); path.MoveTo((float)face.Points[0].X, (float)face.Points[0].Y);
                foreach (var p in face.Points.Skip(1)) path.LineTo((float)p.X, (float)p.Y); path.Close();
                paint.Style = SKPaintStyle.Fill; paint.Color = face.Name == "TOP" ? new SKColor(207, 216, 226) : face.Name is "FRONT" or "BACK" ? new SKColor(155, 171, 189) : new SKColor(180, 193, 207); canvas.DrawPath(path, paint);
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1; paint.Color = new SKColor(65, 81, 102); canvas.DrawPath(path, paint);
                var center = face.Points.Aggregate(default(CadSpace.Geometry.Vec3), (a, b) => a + b) / 4;
                paint.Style = SKPaintStyle.Fill; paint.Color = new SKColor(45, 56, 70); font.Size = 8;
                canvas.DrawText(face.Name, (float)center.X - font.MeasureText(face.Name) / 2, (float)center.Y + 3, font, paint);
            }
            canvas.Restore();
        }
    }
}

public sealed class CadNavigationBar : Border
{
    private readonly Dictionary<string, Button> _buttons = new();
    public event Action<string>? NavigationRequested;
    public CadNavigationBar()
    {
        Background = CadTheme.Brush(0xDD354252); BorderBrush = CadTheme.Brush(0xFF55677B); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(3); Padding = new Thickness(1, 4, 1, 4);
        var body = new StackPanel { Spacing = 3 };
        foreach (var (name, icon, title) in new[] { ("select", "SELECTALL", "Select"), ("pan", "PAN", "Pan: left-drag the viewport"), ("zoomIn", "ZOOM", "Zoom in"), ("zoomOut", "ZOOMOUT", "Zoom out"), ("ZOOM", "FIT", "Zoom extents"), ("orbit", "3DORBIT", "3D Orbit"), ("projection", "PERSPECTIVE", "Toggle perspective / orthographic"), ("clip", "CLIP3D", "Toggle display-only clipping") })
        { var b = CadUi.IconButton(icon, title, () => NavigationRequested?.Invoke(name), "navigation." + name, 27); _buttons.Add(name, b); body.Children.Add(b); }
        Child = body;
    }
    public void SetMode(string mode) { foreach (var (name, b) in _buttons) b.Background = CadTheme.Brush(name == mode ? 0xFF2D6E9D : 0x00354252); }
}

public sealed class CadViewportControls : StackPanel
{
    private readonly Button _view, _style;
    private string _last = "";
    public event Action<string>? NavigationRequested;
    public CadViewportControls()
    {
        Orientation = Orientation.Horizontal; Spacing = 1;
        var more = CadUi.TextButton("[ + ]", () => { }, "viewport.menu"); var options = new MenuFlyout();
        foreach (var (text, action) in new[] { ("Zoom extents", "ZOOM"), ("Perspective / orthographic", "projection"), ("Section clipping (display only)", "clip") })
        { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => NavigationRequested?.Invoke(action); options.Items.Add(item); }
        more.Flyout = options; Children.Add(more);
        _view = CadUi.TextButton("[ Top ]", () => { }, "viewport.views"); var views = new MenuFlyout();
        foreach (var name in ViewCubeGeometry.Names) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => NavigationRequested?.Invoke("view:" + name); views.Items.Add(item); }
        _view.Flyout = views; Children.Add(_view);
        _style = CadUi.TextButton("[ 2D Wireframe ]", () => { }, "viewport.styles"); var styles = new MenuFlyout();
        foreach (var name in new[] { "2D Wireframe", "Wireframe", "HiddenLine", "Shaded", "ShadedEdges" })
        { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => NavigationRequested?.Invoke("style:" + name); styles.Items.Add(item); }
        _style.Flyout = styles; Children.Add(_style);
        foreach (var b in Children.OfType<Button>()) { b.Background = CadTheme.Brush(0x801D242C); b.Padding = new Thickness(3, 1, 3, 1); }
    }
    public void Synchronize(bool model, double yaw, double pitch, ModelVisualStyle style)
    {
        var key = $"{model}|{yaw:0.0}|{pitch:0.0}|{style}"; if (_last == key) return; _last = key;
        var name = !model ? "Top" : ViewCubeGeometry.Names.FirstOrDefault(n => { var o = ViewCubeGeometry.Named(n); return Math.Abs(o.Yaw - yaw) < .01 && Math.Abs(o.Pitch - pitch) < .01; }) ?? "Custom View";
        _view.Content = "[ " + name + " ]"; _style.Content = "[ " + (!model ? "2D Wireframe" : style.ToString()) + " ]";
    }
}
