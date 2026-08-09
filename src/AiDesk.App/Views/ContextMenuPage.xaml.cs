using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AiDesk.App.ViewModels;

namespace AiDesk.App.Views;

public partial class ContextMenuPage : UserControl
{
    private ContextMenuViewModel? _vm;

    public ContextMenuPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private ContextMenuViewModel? Vm =>
        _vm ??= DataContext as ContextMenuViewModel;

    private void Refresh()
    {
        Vm?.RefreshCommand.Execute(null);
        EmptyHint.Visibility = Vm is { Items.Count: > 0 }
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnToggleClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: ContextMenuItemViewModel item } toggle)
            return;

        // 先回滚到实际状态，操作失败时不留下假状态
        var ok = Vm?.ToggleItem(item, toggle.IsChecked == true) ?? false;
        if (!ok)
            toggle.IsChecked = !toggle.IsChecked;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ContextMenuItemViewModel item } button)
            return;

        var target = FindAncestor<Window>(button);
        var confirm = MessageBox.Show(
            target,
            $"确定要删除右键菜单项「{item.Name}」吗？\n\n位置：{item.RegistryPath}\n\n删除后不可恢复，建议优先使用「禁用」功能。",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        if (Vm?.DeleteItem(item) == true)
            Refresh();
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
                return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}
