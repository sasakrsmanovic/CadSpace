using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Keyboard-searchable command discovery; selection and Enter use the same command dispatcher.</summary>
public sealed class CadCommandSearch : AutoSuggestBox
{
    private readonly Dictionary<string, string> _matches = new();
    public event Action<string>? CommandRequested;
    public CadCommandSearch()
    {
        PlaceholderText = "Type a keyword or command"; FontSize = 11; Width = 214; Height = 26; MinHeight = 24;
        CadUi.Identify(this, "shell.search", "Search commands");
        TextChanged += (_, e) => {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            _matches.Clear(); var query = Text.Trim();
            if (query.Length > 0) foreach (var c in CommandEngine.Commands.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Alias.Equals(query, StringComparison.OrdinalIgnoreCase)).Take(12))
                _matches[c.Name + " — " + c.Description] = c.Name;
            ItemsSource = _matches.Keys.ToArray();
        };
        QuerySubmitted += (_, e) => {
            var value = e.ChosenSuggestion as string ?? e.QueryText;
            if (_matches.TryGetValue(value, out var name)) value = name;
            Text = ""; IsSuggestionListOpen = false;
            if (!string.IsNullOrWhiteSpace(value)) CommandRequested?.Invoke(value);
        };
    }
}

public sealed class CadQuickAccessToolbar : StackPanel
{
    public event Action<string>? CommandRequested;
    public CadQuickAccessToolbar()
    {
        Orientation = Orientation.Horizontal; Spacing = 1;
        foreach (var command in new[] { "NEW", "OPEN", "SAVE", "UNDO", "REDO", "EXPORT" })
            Children.Add(CadUi.IconButton(command, CadUi.Label(command), () => CommandRequested?.Invoke(command), "quick." + command, 27));
        var customize = CadUi.TextButton("▾", () => { }, "quick.customize"); customize.Padding = new Thickness(3, 0, 3, 0);
        var menu = new MenuFlyout();
        foreach (var tool in Children.OfType<Button>())
        {
            var item = new ToggleMenuFlyoutItem { Text = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(tool), IsChecked = true };
            item.Click += (_, _) => tool.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed; menu.Items.Add(item);
        }
        customize.Flyout = menu; Children.Add(customize);
    }
}

/// <summary>File/application menu, backed by host-owned file operations and actual open documents.</summary>
public sealed class CadApplicationMenu : Button
{
    private readonly StackPanel _documents = new() { Spacing = 4, Width = 255 };
    private readonly Flyout _flyout = new();
    public event Action<string>? CommandRequested;
    public event Action<object>? ActivateRequested;
    public CadApplicationMenu()
    {
        Content = "CS"; FontSize = 18; Width = 42; Height = 34; Padding = new Thickness(0); BorderThickness = new Thickness(0);
        Background = CadTheme.Brush(0xFFB53345); Foreground = CadTheme.Brush(0xFFFFFFFF); CornerRadius = new CornerRadius(0);
        CadUi.Identify(this, "shell.application", "Application menu");
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(175) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(280) });
        var actions = new StackPanel { Spacing = 5 };
        foreach (var (command, label) in new[] { ("NEW", "New drawing"), ("OPEN", "Open…"), ("SAVE", "Save native project…"), ("EXPORT", "Export ASCII DXF…"), ("EXPORT_BINARY", "Export binary DXF…"), ("RECOVER", "Drawing recovery…"), ("STUDIO", "Studio plan example"), ("MODEL", "3D example"), ("OPTIONS", "Options…"), ("ABOUT", "About CadSpace") })
        {
            var b = CadUi.TextButton(label, () => { _flyout.Hide(); CommandRequested?.Invoke(command); }, "application." + command);
            b.Height = 36; b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; actions.Children.Add(b);
        }
        grid.Children.Add(actions); _documents.Margin = new Thickness(16, 0, 0, 0);
        var documentsScroll = new ScrollViewer { Content = _documents, MaxHeight = 405, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(documentsScroll, 1); grid.Children.Add(documentsScroll);
        _flyout.Content = grid; Flyout = _flyout;
    }
    public void SetDocuments(IEnumerable<(object Key, string Name, bool Dirty)> documents)
    {
        _documents.Children.Clear(); _documents.Children.Add(CadTheme.Text("OPEN DRAWINGS", 11, CadTheme.Muted));
        var index = 0;
        foreach (var d in documents.Take(30))
        {
            var b = CadUi.TextButton(d.Name + (d.Dirty ? " *" : ""), () => { _flyout.Hide(); ActivateRequested?.Invoke(d.Key); }, "application.document." + index++);
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; ToolTipService.SetToolTip(b, d.Name); _documents.Children.Add(b);
        }
    }
}

public sealed class CadApplicationBar : Grid
{
    public CadApplicationMenu ApplicationMenu { get; } = new();
    public CadQuickAccessToolbar QuickAccess { get; } = new();
    public CadCommandSearch Search { get; } = new();
    private readonly TextBlock _title = CadTheme.Text("CadSpace", 11);
    public event Action<string>? CommandRequested;
    public CadApplicationBar()
    {
        Background = CadTheme.Brush(0xFF1E2733);
        ColumnDefinitions.Add(new() { Width = GridLength.Auto }); ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; left.Children.Add(ApplicationMenu); left.Children.Add(QuickAccess); Children.Add(left);
        _title.HorizontalAlignment = HorizontalAlignment.Center; _title.TextTrimming = TextTrimming.CharacterEllipsis; _title.Margin = new Thickness(12, 0, 12, 0); Grid.SetColumn(_title, 1); Children.Add(_title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        right.Children.Add(Search); right.Children.Add(CadUi.IconButton("RECOVER", "Drawing recovery", () => CommandRequested?.Invoke("RECOVER"), "quick.RECOVER", 25));
        right.Children.Add(CadUi.IconButton("OPTIONS", "Workspace options", () => CommandRequested?.Invoke("OPTIONS"), "quick.OPTIONS", 25));
        right.Children.Add(CadUi.IconButton("HELP", "About CadSpace", () => CommandRequested?.Invoke("ABOUT"), "quick.ABOUT", 25));
        Grid.SetColumn(right, 2); Children.Add(right);
        ApplicationMenu.CommandRequested += c => CommandRequested?.Invoke(c); QuickAccess.CommandRequested += c => CommandRequested?.Invoke(c); Search.CommandRequested += c => CommandRequested?.Invoke(c);
        SizeChanged += (_, _) => Search.Visibility = ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
    }
    public void SetTitle(string name) => _title.Text = name + "  —  CadSpace";
}
