using System.Collections.Immutable;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace CadSpace.Controls;

public sealed partial class CadViewport
{
    private readonly InteractionSurface _overlay;
    private readonly record struct RenderStamp(DrawingScene Scene, long Selection, Vec3 Center, double Zoom,
        double Width, double Height, bool Grid, double Spacing, bool Model, Vec3 Origin, Vec3 Target,
        double Distance, double Yaw, double Pitch, bool Orthographic, ModelVisualStyle Style, Plane3? Clip);
    private RenderStamp? _lastRender;
    private long _sceneDraws, _overlayDraws, _modelDraws;
    private Drawing? _gripDocument;
    private long _gripSelection = -1;
    private readonly List<(Entity Entity, EntityGrip Grip)> _grips = new();
    private Entity? _gripEntity;
    private int _gripIndex, _hotGrip = -1;
    private Vec3 _gripOrigin;
    public void CancelInteraction()
    {
        _gripEntity = null; _pan = _orbit = _selecting = _dragged = false; _hotGrip = -1;
        ReleasePointerCaptures(); Redraw();
    }
    private void ReportRenderStatistics()
    {
        var text = $"CADSPACE_RENDER_STATS: scene={_sceneDraws}; overlay={_overlayDraws}; model={_modelDraws}";
        Console.WriteLine(text); Message?.Invoke(text);
    }
    private void UpdateGrips()
    {
        if (_session == null) return;
        if (ReferenceEquals(_gripDocument, _session.Document.Drawing) && _gripSelection == _session.SelectionRevision) return;
        _gripDocument = _session.Document.Drawing; _gripSelection = _session.SelectionRevision; _grips.Clear(); _hotGrip = -1;
        // Keep pointer work bounded. Large selections remain editable through commands/properties.
        if (_session.Selection.Count > 200) return;
        foreach (var entity in _session.SelectedEntities())
        {
            if (_session.Document.Drawing.LayerFor(entity).Locked) continue;
            var grips = GripEditing.Grips(entity);
            if (_grips.Count + grips.Length > 4096) { _grips.Clear(); return; }
            foreach (var grip in grips) _grips.Add((entity, grip));
        }
    }
    private int FindGrip(Point screen)
    {
        UpdateGrips(); var nearest = -1; double distance = 64;
        for (var i = 0; i < _grips.Count; i++)
        {
            var p = Camera.WorldToScreen(_grips[i].Grip.Position);
            var d = (p.X - screen.X) * (p.X - screen.X) + (p.Y - screen.Y) * (p.Y - screen.Y);
            if (d <= distance) { nearest = i; distance = d; }
        }
        return nearest;
    }
    private bool TryBeginGrip(Point screen)
    {
        if (Is3D || _session == null) return false;
        var index = FindGrip(screen); if (index < 0) return false;
        (_gripEntity, var grip) = _grips[index]; _gripIndex = grip.Index; _cursor = _gripOrigin = grip.Position;
        Message?.Invoke($"Grip: {grip.Label}. Drag to a new position; Escape cancels."); Redraw(); return true;
    }
    private void MoveGripPointer(Point p)
    {
        if (_session == null) return;
        _dragged |= Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 3;
        var world = Camera.ScreenToWorld(p.X, p.Y) with { Z = _gripOrigin.Z };
        _snap = _session.Snap(world, 9 / Camera.PixelsPerUnit, _gripOrigin); _cursor = _snap.Point;
    }
    private void SelectAt(Vec3 world, Point screen, SelectionMode mode)
    {
        if (_session == null) return;
        var session = _session;
        var hits = session.SelectionCycling ? SelectionQueries.For(session.Scene).Pick(world, 6 / Camera.PixelsPerUnit) : [];
        if (hits.Length > 1)
        {
            var menu = new MenuFlyout();
            foreach (var id in hits.Take(25))
            {
                var entity = session.FindEntity(id); if (entity == null) continue;
                var item = new MenuFlyoutItem { Text = $"{entity.Kind}  •  {entity.Layer}  •  {id.ToString()[..8]}" };
                item.Click += (_, _) => session.ApplySelection([id], mode); menu.Items.Add(item);
            }
            menu.ShowAt(this, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = screen }); return;
        }
        Guid? hit = hits.Length == 1 ? hits[0] : session.HitTest(world, 6 / Camera.PixelsPerUnit);
        session.ApplySelection(hit is Guid value ? [value] : [], hit == null && mode == SelectionMode.Add ? SelectionMode.Replace : mode);
    }
    private sealed class InteractionSurface(CadViewport owner) : SKCanvasElement
    {
        private readonly SkiaDraftRenderer _renderer = new();
        private readonly SKPaint _gripPaint = new() { IsAntialias = false };
        private readonly HashSet<Guid> _none = new();
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var session = owner._session; if (session == null || owner.Is3D) return;
            owner._overlayDraws++;
            try
            {
                IReadOnlyList<Entity> preview = owner._gripEntity != null ?
                    [session.PreviewGrip(owner._gripEntity, owner._gripIndex, owner._cursor)] :
                    owner._commands?.Preview(owner._cursor) ?? [];
                if (preview.Count > 0) _renderer.DrawScene(canvas, owner.Camera,
                    EntityGeometry.BuildScene(session.Document.Drawing with { Entities = preview.Select(e => e with { Layout = session.ActiveLayout }).ToImmutableArray() }, session.ActiveLayout), _none, true);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { /* Commit reports invalid targets; the original document is unchanged. */ }
            owner.UpdateGrips();
            if (owner._commands?.IsActive != true)
            {
                for (var i = 0; i < owner._grips.Count; i++)
                {
                    var p = owner.Camera.WorldToScreen(owner._grips[i].Grip.Position);
                    if (p.X < -5 || p.Y < -5 || p.X > area.Width + 5 || p.Y > area.Height + 5) continue;
                    _gripPaint.Color = i == owner._hotGrip ? new SKColor(255, 177, 74) : new SKColor(63, 151, 248);
                    canvas.DrawRect((float)p.X - 4, (float)p.Y - 4, 8, 8, _gripPaint);
                }
            }
            _renderer.DrawInteraction(canvas, owner.Camera, owner._cursor, owner._inside,
                owner._snap.Kind is SnapKind.None or SnapKind.Grid ? null : owner._snap.Kind.ToString(),
                owner._selecting && owner._dragged ? owner._pressWorld : null);
        }
    }
}
