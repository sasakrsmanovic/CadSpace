using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Bounded vertex-property editor; never creates a visual row for every vertex in a large polyline.</summary>
public sealed class CadPolylineEditor : UserControl
{
    private readonly TextBox _index = Input("Vertex (1-based)", "1");
    private readonly TextBox _start = Input("Start width", "0"), _end = Input("End width", "0"), _bulge = Input("Bulge", "0");
    private readonly TextBlock _status = CadTheme.Text("", 10, CadTheme.Muted);
    private CadSession? _session;
    private PolylineEntity? _expected;
    private int _selected;
    public CadPolylineEditor()
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(CadTheme.Text("SEGMENT PROPERTIES", 10, CadTheme.Muted));
        body.Children.Add(_index); body.Children.Add(CadTheme.Button("Read vertex", Read));
        body.Children.Add(_start); body.Children.Add(_end); body.Children.Add(_bulge);
        body.Children.Add(CadTheme.Button("Apply segment", Apply));
        _status.TextWrapping = TextWrapping.Wrap; body.Children.Add(_status); Content = body;
    }
    private static TextBox Input(string header, string value) => new() { Header = header, Text = value, FontSize = 11, MinHeight = 28, Padding = new Thickness(5, 2, 5, 2) };
    public void Bind(CadSession session, PolylineEntity polyline) { _session = session; _expected = polyline; Read(); }
    private bool SelectIndex()
    {
        if (_expected == null) return false;
        if (!int.TryParse(_index.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1 || n > _expected.Vertices.Length)
        { _status.Text = $"Enter a vertex from 1 to {_expected.Vertices.Length}."; return false; }
        _selected = n - 1; return true;
    }
    private void Read()
    {
        if (!SelectIndex()) return;
        var vertex = _expected!.Vertices[_selected];
        _start.Text = vertex.StartWidth.ToString(CultureInfo.InvariantCulture);
        _end.Text = vertex.EndWidth.ToString(CultureInfo.InvariantCulture);
        _bulge.Text = vertex.Bulge.ToString(CultureInfo.InvariantCulture);
        _status.Text = !_expected.Closed && _selected == _expected.Vertices.Length - 1 ? "Last open vertex: outgoing properties take effect only after closing or extending." : "Widths apply to the segment starting at this vertex. Applying clears global width.";
    }
    private void Apply()
    {
        if (_session == null || _expected == null || !SelectIndex()) return;
        try
        {
            double Parse(TextBox box) => GeometryMath.Number(box.Text, out var n) ? n : throw new ArgumentException("Enter finite numeric values.");
            var replacement = _expected.Vertices[_selected] with { StartWidth = Parse(_start), EndWidth = Parse(_end), Bulge = Parse(_bulge) };
            // The engine checks the captured entity, current selection and layer locks before one atomic edit.
            _session.EditPolylineVertex(_expected, _selected, replacement);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { _status.Text = error.Message; }
    }
}
