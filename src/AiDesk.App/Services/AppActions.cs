using System.Windows;
using AiDesk.App.ViewModels;

namespace AiDesk.App.Services;

/// <summary>
/// AiDesk 应用自身动作（Agent 联动入口）：打开搜索面板 / 剪贴板 / 主窗口等。
/// </summary>
public static class AppActions
{
    /// <summary>打开主窗口并置前。</summary>
    public static void ShowMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
            ((App)Application.Current).ShowMainWindowFromAgent());
    }

    /// <summary>打开/切换搜索面板（搜索 Tab）。</summary>
    public static void OpenSearchPanel()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var vm = GetWidgetViewModel();
            if (vm is not null)
            {
                // 未打开则打开，已打开则切到搜索 Tab
                vm.ShowSearchPanel();
            }
        });
    }

    /// <summary>打开剪贴板面板（剪贴板 Tab）。</summary>
    public static void OpenClipboard()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var vm = GetWidgetViewModel();
            vm?.ShowClipboard();
        });
    }

    private static WidgetViewModel? GetWidgetViewModel()
    {
        if (Application.Current.MainWindow?.DataContext is MainViewModel main)
            return main.NavItems
                .FirstOrDefault(n => n.ViewModel is WidgetViewModel)?.ViewModel as WidgetViewModel;
        return null;
    }
}
