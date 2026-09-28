using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Reusable, staged UI-only options editor. Capture validates without touching a drawing or the live workspace.</summary>
public sealed class CadWorkspaceOptions : UserControl
{
    private readonly WorkspaceLayout _initial;
    private readonly TextBlock _error = CadTheme.Text("", 11, 0xFFFFB09E);
    public void ShowError(string message) => _error.Text = message;
    private readonly CheckBox _menu, _cube, _navigation, _minimized, _clean;
    private readonly TextBox _commandHeight;
    private readonly ComboBox _preset;
    private readonly Dictionary<string, (ComboBox Dock, CheckBox Visible, CheckBox AutoHide)> _palettes = new();
    private readonly Dictionary<string, CheckBox> _status = new();
    public CadWorkspaceOptions(WorkspaceLayout initial)
    {
        WorkspaceLayout.Validate(initial); _initial = initial;
        var root = CadTheme.Grid(31, -1, 30); root.Width = 610; root.Height = 370;
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var pages = new Grid { Padding = new Thickness(14), BorderBrush = CadTheme.Brush(CadTheme.Edge), BorderThickness = new Thickness(1) };
        CadTheme.At(root, tabs, 0); CadTheme.At(root, new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1); _error.TextWrapping = TextWrapping.Wrap; CadTheme.At(root, _error, 2);
        var panels = new Dictionary<string, StackPanel>(); var buttons = new Dictionary<string, Button>();
        void Show(string name)
        {
            foreach (var (key, page) in panels) { page.Visibility = key == name ? Visibility.Visible : Visibility.Collapsed; buttons[key].Background = CadTheme.Brush(key == name ? 0xFF415771 : CadTheme.Panel); }
        }
        StackPanel Page(string name)
        {
            var page = new StackPanel { Spacing = 12 }; panels.Add(name, page);
            var button = CadUi.TextButton(name, () => Show(name), "options.tab." + name); buttons.Add(name, button); tabs.Children.Add(button);
            pages.Children.Add(page);
            return page;
        }
        var display = Page("Display");
        _menu = Check("Display classic menu bar", "options.menu", initial.MenuBarVisible);
        _cube = Check("Display ViewCube", "options.cube", initial.ViewCubeVisible);
        _navigation = Check("Display navigation bar", "options.navigation", initial.NavigationBarVisible);
        _minimized = Check("Minimize ribbon to tabs", "options.minimized", initial.RibbonMinimized);
        _clean = Check("Clean screen (Ctrl+0)", "options.clean", initial.CleanScreen);
        foreach (var check in new[] { _menu, _cube, _navigation, _minimized, _clean }) display.Children.Add(check);
        _commandHeight = CadUi.Identify(new TextBox { Header = "Command window height (74–350)", Text = initial.CommandHeight.ToString(CultureInfo.InvariantCulture), Width = 250, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12 }, "options.commandHeight", "Command window height"); display.Children.Add(_commandHeight);
        var workspace = Page("Workspace");
        _preset = CadUi.Identify(new ComboBox { Header = "Workspace preset", ItemsSource = WorkspaceLayout.Presets, SelectedItem = initial.Workspace, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 }, "options.preset", "Workspace preset"); workspace.Children.Add(_preset);
        foreach (var p in initial.Palettes)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            row.Children.Add(new TextBlock { Text = p.Id == "properties" ? "Properties" : p.Id == "tools" ? "Tool Palettes" : p.Id, Width = 105, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            var dock = CadUi.Identify(new ComboBox { ItemsSource = Enum.GetNames<PaletteDock>(), SelectedItem = p.Dock.ToString(), Width = 120, FontSize = 12 }, "options." + p.Id + ".dock", p.Id + " docking");
            var visible = Check("Visible", "options." + p.Id + ".visible", p.Visible); var autoHide = Check("Auto-hide", "options." + p.Id + ".autoHide", p.AutoHide);
            row.Children.Add(dock); row.Children.Add(visible); row.Children.Add(autoHide); workspace.Children.Add(row); _palettes[p.Id] = (dock, visible, autoHide);
        }
        var tip = CadTheme.Text("Drag palette headers to either edge to dock. Hold Ctrl to keep a palette floating. Escape cancels a drag or resize.\n\nCtrl+1: Properties  •  Ctrl+3: Tool Palettes\nF2: History  •  F6: Command line  •  Ctrl+K: Search\n\nPreferences are local UI settings, separate from drawing files and Undo.", 12, CadTheme.Muted); tip.TextWrapping = TextWrapping.Wrap; workspace.Children.Add(tip);
        var status = Page("Status Bar"); var note = CadTheme.Text("Choose controls to display. Hiding a control does not switch its drafting mode off. Keyboard shortcuts remain available.", 12, CadTheme.Muted); note.TextWrapping = TextWrapping.Wrap; status.Children.Add(note);
        foreach (var id in WorkspaceLayout.StatusItems)
        { var check = Check(id, "options.status." + id, !initial.HiddenStatusItems.Contains(id)); _status.Add(id, check); status.Children.Add(check); }
        Show("Display"); Content = root;
    }
    private static CheckBox Check(string text, string id, bool value) => CadUi.Identify(new CheckBox { Content = text, IsChecked = value, FontSize = 12, MinHeight = 25 }, id, text);
    public WorkspaceLayout Capture()
    {
        if (!GeometryMath.Number(_commandHeight.Text, out var height)) throw new ArgumentException("Enter a finite command window height.");
        var value = _initial with {
            Workspace = _preset.SelectedItem as string ?? _initial.Workspace,
            MenuBarVisible = _menu.IsChecked == true, ViewCubeVisible = _cube.IsChecked == true, NavigationBarVisible = _navigation.IsChecked == true,
            RibbonMinimized = _minimized.IsChecked == true, CleanScreen = _clean.IsChecked == true, CommandHeight = height,
            HiddenStatusItems = _status.Where(p => p.Value.IsChecked != true).Select(p => p.Key).ToImmutableArray(),
            Palettes = _initial.Palettes.Select(p => { var controls = _palettes[p.Id]; var dock = Enum.Parse<PaletteDock>((string)controls.Dock.SelectedItem); return p with { Dock = dock, Visible = controls.Visible.IsChecked == true, AutoHide = dock != PaletteDock.Floating && controls.AutoHide.IsChecked == true }; }).ToImmutableArray()
        };
        WorkspaceLayout.Validate(value); return value;
    }
}
