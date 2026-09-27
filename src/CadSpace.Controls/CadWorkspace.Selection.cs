using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private bool _selectionDialog;
    private async void ShowQuickSelect()
    {
        if (_session == null || XamlRoot == null || _selectionDialog) return;
        _selectionDialog = true; var session = _session; _commands?.Cancel();
        try
        {
            var kind = new ComboBox { Header = "Object type", ItemsSource = new[] { "*" }.Concat(session.Document.Drawing.Entities.Select(e => e.Kind).Distinct().Order()).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var layer = new ComboBox { Header = "Layer", ItemsSource = new[] { "*" }.Concat(session.Document.Drawing.Layers.Keys.Order()).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var mode = new ComboBox { Header = "How to apply", ItemsSource = new[] { "Replace", "Add", "Remove" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var scope = new CheckBox { Content = "Apply only to the current selection" };
            var body = new StackPanel { Spacing = 14, MinWidth = 310 };
            body.Children.Add(CadTheme.Text("Filter visible objects in the active layout. * means any.", 11));
            body.Children.Add(kind); body.Children.Add(layer); body.Children.Add(mode); body.Children.Add(scope);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Quick Select", Content = body, PrimaryButtonText = "Select", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(session, _session))
            {
                session.QuickSelect((string)kind.SelectedItem, (string)layer.SelectedItem, Enum.Parse<SelectionMode>((string)mode.SelectedItem), scope.IsChecked == true);
                CommandLine.AddMessage($"Selected {session.Selection.Count} objects.");
            }
        }
        catch (Exception error) { CommandLine.AddMessage(error.Message); }
        finally { _selectionDialog = false; }
    }
}
