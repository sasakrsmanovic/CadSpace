using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace CadSpace.Controls;

public static class CadTheme
{
    public const uint Background = 0xFF252F3D, Panel = 0xFF3B4758, Raised = 0xFF4A5769, Edge = 0xFF526074, TextColor = 0xFFDCE2EB, Muted = 0xFF9AA8BA, Accent = 0xFF4A9DE9;
    public static SolidColorBrush Brush(uint color) => new(Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
    public static TextBlock Text(string text, double size = 12, uint color = TextColor) => new() { Text = text, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string title, Action action, double width = double.NaN)
    {
        var button = new Button { Content = title, Padding = new Thickness(9, 4, 9, 4), MinHeight = 26, FontSize = 12, Background = Brush(Panel), Foreground = Brush(TextColor), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(2), Width = width };
        button.Click += (_, _) => action(); AutomationProperties.SetName(button, title); return button;
    }
    public static Border Box(UIElement content, uint color = Panel, double padding = 0) => new() { Child = content, Background = Brush(color), Padding = new Thickness(padding) };
    public static Grid Grid(params double[] rows)
    {
        var grid = new Grid(); foreach (var row in rows) grid.RowDefinitions.Add(new RowDefinition { Height = row < 0 ? new GridLength(1, GridUnitType.Star) : row == 0 ? GridLength.Auto : new GridLength(row) }); return grid;
    }
    public static void At(Grid grid, UIElement child, int row, int column = 0) { Microsoft.UI.Xaml.Controls.Grid.SetRow(child, row); Microsoft.UI.Xaml.Controls.Grid.SetColumn(child, column); grid.Children.Add(child); }
}

/// <summary>Original vector command artwork; no Autodesk assets or icon fonts.</summary>
public sealed class CadIcon : SKCanvasElement
{
    public string Kind { get; set; } = "LINE";
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Scale((float)(area.Width / 32), (float)(area.Height / 32));
        using var p = new SKPaint { Color = new SKColor(138, 205, 241), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, StrokeCap = SKStrokeCap.Round };
        if (CadShellArtwork.Draw(Kind, canvas, p)) { canvas.Restore(); return; }
        void Line(float a, float b, float c, float d) => canvas.DrawLine(a, b, c, d, p);
        void Rect(float x, float y, float w, float h) => canvas.DrawRect(x, y, w, h, p);
        switch (Kind)
        {
            case "CIRCLE": canvas.DrawCircle(16, 16, 10, p); break;
            case "ARC": using (var arc = new SKPath()) { arc.AddArc(new SKRect(5, 5, 27, 27), 195, 245); canvas.DrawPath(arc, p); } break;
            case "RECTANG": Rect(4, 8, 24, 17); break;
            case "PLINE": Line(4, 25, 10, 8); Line(10, 8, 22, 16); Line(22, 16, 28, 5); break;
            case "TEXT": case "DIMALIGNED": case "DIST":
                if (Kind == "TEXT") { Line(5, 6, 27, 6); Line(16, 6, 16, 27); Line(11, 27, 21, 27); }
                else { Line(5, 4, 5, 27); Line(27, 4, 27, 27); Line(5, 15, 27, 15); Line(5, 15, 10, 11); Line(27, 15, 22, 19); } break;
            case "MOVE": Line(16, 3, 16, 29); Line(3, 16, 29, 16); Line(16, 3, 12, 7); Line(16, 3, 20, 7); Line(29, 16, 25, 12); Line(29, 16, 25, 20); break;
            case "COPY": Rect(3, 5, 16, 16); Rect(13, 13, 16, 16); break;
            case "ROTATE": using (var arc = new SKPath()) { arc.AddArc(new SKRect(5, 5, 27, 27), 20, 285); canvas.DrawPath(arc, p); } Line(22, 4, 21, 12); Line(22, 4, 29, 6); break;
            case "SCALE": Rect(3, 16, 12, 12); Rect(10, 4, 18, 18); Line(14, 19, 27, 5); break;
            case "MIRROR": Line(16, 3, 16, 29); Line(5, 23, 12, 8); Line(20, 8, 27, 23); Line(5, 23, 12, 23); Line(20, 23, 27, 23); break;
            case "STRETCH": Rect(3, 10, 13, 16); Line(16, 10, 26, 5); Line(16, 26, 26, 21); Line(26, 5, 26, 21); Rect(24, 3, 4, 4); Rect(24, 19, 4, 4); break;
            case "QSELECT": Line(3, 5, 29, 5); Line(3, 5, 13, 17); Line(29, 5, 19, 17); Line(13, 17, 13, 28); Line(19, 17, 19, 24); Line(13, 28, 19, 24); break;
            case "SELECTSIMILAR": Rect(3, 4, 12, 12); Rect(18, 19, 11, 11); Line(19, 7, 27, 7); Line(24, 4, 27, 7); Line(24, 10, 27, 7); break;
            case "RENDERSTATS": Line(4, 4, 4, 28); Line(4, 28, 29, 28); Line(8, 22, 12, 17); Line(12, 17, 18, 21); Line(18, 21, 26, 8); break;
            case "OFFSET": Line(5, 27, 14, 5); Line(14, 27, 23, 5); break;
            case "ERASE": p.Color = new SKColor(234, 174, 179); Line(5, 23, 19, 7); Line(19, 7, 28, 15); Line(28, 15, 16, 28); Line(16, 28, 5, 23); break;
            case "HATCH": Rect(4, 4, 24, 24); for (var i = 7; i < 28; i += 5) Line(5, i, i, 5); break;
            case "ZOOM": canvas.DrawCircle(13, 13, 8, p); Line(19, 19, 28, 28); Line(9, 13, 17, 13); Line(13, 9, 13, 17); break;
            case "POINT": Line(5, 16, 27, 16); Line(16, 5, 16, 27); canvas.DrawCircle(16, 16, 3, p); break;
            case "CYLINDER": canvas.DrawOval(new SKRect(6, 3, 26, 12), p); canvas.DrawOval(new SKRect(6, 21, 26, 30), p); Line(6, 7, 6, 25); Line(26, 7, 26, 25); break;
            case "SPHERE": canvas.DrawCircle(16, 16, 12, p); canvas.DrawOval(new SKRect(10, 4, 22, 28), p); canvas.DrawOval(new SKRect(4, 11, 28, 21), p); break;
            case "CONE": canvas.DrawOval(new SKRect(4, 20, 28, 29), p); Line(4, 24, 16, 3); Line(28, 24, 16, 3); break;
            case "BOX": case "BLOCK": case "INSERT": case "EXTRUDE": case "3DORBIT":
                Line(4, 11, 16, 4); Line(16, 4, 28, 11); Line(28, 11, 16, 18); Line(16, 18, 4, 11); Line(4, 11, 4, 24); Line(28, 11, 28, 24); Line(16, 18, 16, 30); Line(4, 24, 16, 30); Line(16, 30, 28, 24); break;
            case "TOP": Rect(5, 5, 22, 22); Line(16, 5, 16, 27); Line(5, 16, 27, 16); break;
            default: Line(4, 26, 27, 5); Rect(2, 24, 4, 4); Rect(25, 3, 4, 4); break;
        }
        canvas.Restore();
    }
}

public sealed class CadToolButton : Button
{
    public CadToolButton(string command, string label, string description, Action invoke)
    {
        Width = 58; Height = 68; Padding = new Thickness(2, 5, 2, 2); BorderThickness = new Thickness(0); CornerRadius = new CornerRadius(2);
        Background = CadTheme.Brush(CadTheme.Panel); Foreground = CadTheme.Brush(CadTheme.TextColor);
        var stack = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new CadIcon { Kind = command, Width = 30, Height = 30, IsHitTestVisible = false });
        stack.Children.Add(CadTheme.Text(label, 10)); Content = stack;
        ToolTipService.SetToolTip(this, $"{label} ({command})\n{description}"); AutomationProperties.SetName(this, label);
        Click += (_, _) => invoke();
    }
}
