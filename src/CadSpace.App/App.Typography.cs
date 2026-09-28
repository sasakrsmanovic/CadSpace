using CadSpace.Controls;

namespace CadSpace.App;

public sealed partial class App
{
    static App()
    {
        // Set the application default before InitializeComponent or any XAML control
        // is constructed. Reusable controls do not change their host's global setting.
        global::Uno.UI.FeatureConfiguration.Font.DefaultTextFontFamily = CadTheme.UiFontSource;
    }
}
