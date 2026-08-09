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

    private void OnRecommendedClicked(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;

        var matched = Vm.PrepareRecommended();
        if (matched.Count == 0)
        {
            MessageBox.Show(FindAncestor<Window>(this),
                "当前系统中没有找到可精简的菜单项。\n\n可能已被禁用，或系统版本不同（清单基于 Win10/11 常见冗余项）。",
                "推荐精简", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = matched.Select(m => $"• {m.Name}（{m.Description}）— {m.Location}");
        var confirm = MessageBox.Show(
            FindAncestor<Window>(this),
            $"将禁用以下 {matched.Count} 个系统冗余菜单项（可随时在列表中重新启用）：\n\n{string.Join("\n", lines)}\n\n确定继续吗？",
            "推荐精简确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        var count = Vm.ApplyRecommended();
        Refresh();
        if (count < 0)
        {
            MessageBox.Show(FindAncestor<Window>(this),
                "操作失败，请查看状态栏的失败原因。",
                "推荐精简失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(FindAncestor<Window>(this),
            $"已禁用 {count} 个冗余菜单项。\n\n可在资源管理器右键查看效果，需要恢复时在列表中打开对应开关。",
            "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
