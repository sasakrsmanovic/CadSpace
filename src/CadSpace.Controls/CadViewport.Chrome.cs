using Microsoft.UI.Xaml;

namespace CadSpace.Controls;

public sealed partial class CadViewport
{
    public bool ViewCubeVisible => _cube.Visibility == Visibility.Visible;
    public bool NavigationBarVisible => _navigation.Visibility == Visibility.Visible;
    /// <summary>Changes navigation controls only; leaves camera, drawing, selection and rendering mode untouched.</summary>
    public void SetNavigationVisibility(bool cube, bool navigation)
    {
        _cube.Visibility = cube ? Visibility.Visible : Visibility.Collapsed;
        _navigation.Visibility = navigation ? Visibility.Visible : Visibility.Collapsed;
    }
}
