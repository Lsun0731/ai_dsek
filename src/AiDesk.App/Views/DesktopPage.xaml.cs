using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AiDesk.App.ViewModels;

namespace AiDesk.App.Views;

public partial class DesktopPage : UserControl
{
    private DesktopViewModel? _vm;

    public DesktopPage()
    {
        InitializeComponent();
    }

    private DesktopViewModel? Vm => _vm ??= DataContext as DesktopViewModel;

    private void OnColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Color color } || Vm is null)
            return;
        Vm.AccentColor = color;
        Vm.ApplyAccentColorCommand.Execute(null);
    }

    private void OnDarkModeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton)
            Vm?.ToggleDarkModeCommand.Execute(null);
    }

    private void OnTransparencyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton)
            Vm?.ToggleTransparencyCommand.Execute(null);
    }
}
