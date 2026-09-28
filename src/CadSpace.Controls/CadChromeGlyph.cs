using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace CadSpace.Controls;

/// <summary>Font-independent workspace glyphs for small menu, docking, tab and overflow controls.</summary>
public sealed class CadChromeGlyph : SKCanvasElement
{
    public string Symbol { get; init; } = "+";
    public uint Color { get; init; } = CadTheme.TextColor;
    public static bool Supports(string symbol) => symbol is "▾" or "⌃" or "‹" or "›" or "□" or "●" or "○" or "×" or "+" or "☰" or "↗" or "↑";
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Scale((float)(area.Width / 16), (float)(area.Height / 16));
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.35f, StrokeCap = SKStrokeCap.Round,
            Color = new SKColor((byte)(Color >> 16), (byte)(Color >> 8), (byte)Color, (byte)(Color >> 24)) };
        void Line(float x, float y, float a, float b) => canvas.DrawLine(x, y, a, b, paint);
        switch (Symbol)
        {
            case "↗": Line(3, 13, 13, 3); Line(5, 3, 13, 3); Line(13, 3, 13, 11); break;
            case "↑": Line(8, 13, 8, 3); Line(3, 8, 8, 3); Line(8, 3, 13, 8); break;
            case "▾": Line(3, 5, 8, 10); Line(8, 10, 13, 5); break;
            case "⌃": Line(3, 10, 8, 5); Line(8, 5, 13, 10); break;
            case "‹": Line(10, 3, 5, 8); Line(5, 8, 10, 13); break;
            case "›": Line(5, 3, 10, 8); Line(10, 8, 5, 13); break;
            case "□": canvas.DrawRect(2, 5, 9, 9, paint); Line(5, 2, 14, 2); Line(14, 2, 14, 11); break;
            case "●": case "○":
                Line(5, 3, 11, 3); Line(6, 3, 6, 8); Line(10, 3, 10, 8); Line(4, 9, 12, 9); Line(8, 9, 8, 14);
                if (Symbol == "○") { paint.StrokeWidth = 1; Line(2, 13, 14, 1); } break;
            case "×": Line(4, 4, 12, 12); Line(4, 12, 12, 4); break;
            case "+": Line(8, 3, 8, 13); Line(3, 8, 13, 8); break;
            case "☰": Line(3, 4, 13, 4); Line(3, 8, 13, 8); Line(3, 12, 13, 12); break;
        }
        canvas.Restore();
    }
}
