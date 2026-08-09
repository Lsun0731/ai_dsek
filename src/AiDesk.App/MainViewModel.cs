using System.Collections.ObjectModel;
using AiDesk.App.ViewModels;
using AiDesk.Core.Diagnostics;
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

        // ===== 桌面工具（核心） =====
        NavItems.Add(new NavItem("桌面工具", null, isGroup: true));
        NavItems.Add(new NavItem("桌面 Dock", "\uE7C4", new DesktopDockViewModel()));
        NavItems.Add(new NavItem("桌面小组件", "\uE823", new WidgetViewModel()));
        NavItems.Add(new NavItem("外观美化", "\uE7B8", new DesktopViewModel()));
        NavItems.Add(new NavItem("任务栏美化", "\uE771", new TaskbarViewModel()));
        NavItems.Add(new NavItem("光标主题", "\uE771", new CursorThemeViewModel()));

        // ===== 系统清理（扩展） =====
        NavItems.Add(new NavItem("系统清理", null, isGroup: true));
        NavItems.Add(new NavItem("右键菜单管理", "\uE7C3", contextMenuVm));
        NavItems.Add(new NavItem("软件卸载", "\uE74D", new PlaceholderViewModel("软件卸载（开发中）")));
        NavItems.Add(new NavItem("注册表管理", "\uE943", new PlaceholderViewModel("注册表管理（开发中）")));

        // ===== 设置 =====
        NavItems.Add(new NavItem("设置", "\uE713", new PlaceholderViewModel("设置（开发中）"), isBottom: true));

        SelectedNavItem = NavItems[1];
    }

    /// <summary>热键 Ctrl+Alt+D：呼出/隐藏搜索小组件。</summary>
    public void ToggleSearchWidget()
    {
        var widgetVm = NavItems.FirstOrDefault(n => n.ViewModel is WidgetViewModel)?.ViewModel as WidgetViewModel;
        widgetVm?.ToggleSearch();
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null && !value.IsGroup)
        {
            CurrentPage = value.ViewModel;
            Telemetry.Event("Navigate", value.Title);
        }
    }
}
