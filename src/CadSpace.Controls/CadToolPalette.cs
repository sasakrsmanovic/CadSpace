using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Searchable virtualized command/block palette; no application or file-system dependency.</summary>
public sealed class CadToolPalette : UserControl
{
    private readonly TextBox _search = new() { PlaceholderText = "Search tools and blocks", FontSize = 11, MinHeight = 28 };
    private readonly ListView _list = new() { IsItemClickEnabled = true, SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single };
    private readonly TextBlock _detail = CadTheme.Text("Choose a tool, then follow the command prompts.", 11, CadTheme.Muted);
    private readonly Dictionary<string, string> _values = new();
    private CadSession? _session; private object? _blocks; private string _category = "Draw", _signature = "";
    public event Action<string>? CommandRequested;
    public event Action<string>? InsertRequested;
    public CadToolPalette()
    {
        var root = CadTheme.Grid(30, 32, -1, 72); var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var name in new[] { "Draw", "Modify", "3D", "Blocks" }) tabs.Children.Add(CadUi.TextButton(name, () => { _category = name; Refresh(); }, "tools.category." + name));
        CadTheme.At(root, tabs, 0); CadTheme.At(root, _search, 1); CadTheme.At(root, _list, 2); _detail.TextWrapping = TextWrapping.Wrap; _detail.Margin = new Thickness(10, 5, 10, 5); CadTheme.At(root, _detail, 3);
        CadUi.Identify(_search, "tools.search", "Search tool palette"); CadUi.Identify(_list, "tools.list", "Tool palette items");
        _search.TextChanged += (_, _) => Refresh();
        _list.ItemClick += (_, e) => {
            if (e.ClickedItem is not string display || !_values.TryGetValue(display, out var value)) return;
            if (_category == "Blocks") { _detail.Text = "Insert block: " + value; InsertRequested?.Invoke(value); }
            else { _detail.Text = CommandEngine.Commands.First(c => c.Name == value).Description; CommandRequested?.Invoke(value); }
        };
        Content = root; Refresh();
    }
    public void Bind(CadSession session)
    { if (_session != null) _session.Changed -= Refresh; _session = session; _blocks = null; session.Changed += Refresh; Refresh(); }
    private void Refresh()
    {
        var key = _category + "|" + _search.Text;
        if (_signature == key && ReferenceEquals(_blocks, _session?.Document.Drawing.Blocks)) return;
        _signature = key; _blocks = _session?.Document.Drawing.Blocks; _values.Clear(); var query = _search.Text.Trim();
        if (_category == "Blocks")
        {
            foreach (var block in (_session?.Document.Drawing.Blocks.Values ?? Enumerable.Empty<BlockDefinition>()).Where(b => !b.Name.StartsWith('*') && b.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.Name))
                _values[block.Name + "   ·   " + block.Entities.Length + " objects"] = block.Name;
        }
        else foreach (var c in CommandEngine.Commands.Where(c => (_category == "3D" ? c.Category == "Model" || c.Name is "ROTATE3D" or "MIRROR3D" or "ALIGN3D" : c.Category == _category) && (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Description.Contains(query, StringComparison.OrdinalIgnoreCase))))
            _values[CadUi.Label(c.Name) + "   (" + c.Alias + ")"] = c.Name;
        _list.ItemsSource = _values.Keys.ToArray();
        if (_values.Count == 0) _detail.Text = _category == "Blocks" ? "No matching block definitions. Select geometry and run BLOCK to create one." : "No matching tools.";
    }
}
