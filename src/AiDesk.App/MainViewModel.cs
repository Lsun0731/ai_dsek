using System.Collections.ObjectModel;
using AiDesk.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDesk.App;

/// <summary>
/// 主窗口 ViewModel：左侧导航 + 当前页面。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private object? _currentPage;

    public MainViewModel()
    {
        var contextMenuVm = new ContextMenuViewModel();
        NavItems.Add(new NavItem("系统清理", null, isGroup: true));
        NavItems.Add(new NavItem("右键菜单管理", "\uE7C3", contextMenuVm));
        NavItems.Add(new NavItem("软件卸载", "\uE74D", new PlaceholderViewModel("软件卸载（开发中）")));
        NavItems.Add(new NavItem("注册表管理", "\uE943", new PlaceholderViewModel("注册表管理（开发中）")));

        NavItems.Add(new NavItem("桌面美化", null, isGroup: true));
        NavItems.Add(new NavItem("外观美化", "\uE7B8", new DesktopViewModel()));

        NavItems.Add(new NavItem("设置", "\uE713", new PlaceholderViewModel("设置（开发中）"), isBottom: true));

        SelectedNavItem = NavItems[1];
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null && !value.IsGroup)
            CurrentPage = value.ViewModel;
    }
}
