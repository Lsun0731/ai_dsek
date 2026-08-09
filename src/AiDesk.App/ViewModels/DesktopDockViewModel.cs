using CommunityToolkit.Mvvm.ComponentModel;
using AiDesk.App.DesktopDock;
using AiDesk.App.Services;

namespace AiDesk.App.ViewModels;

/// <summary>桌面 Dock 设置页 ViewModel：启用 Dock / 隐藏任务栏 / 隐藏图标。</summary>
public partial class DesktopDockViewModel : ObservableObject
{
    private readonly DesktopIntegrationController? _controller = DesktopIntegrationController.Instance;

    [ObservableProperty]
    private bool _dockEnabled;

    [ObservableProperty]
    private bool _hideTaskbar;

    [ObservableProperty]
    private bool _hideIcons;

    public DesktopDockViewModel()
    {
        var dock = _controller?.Settings.Dock;
        _dockEnabled = dock?.Enabled ?? true;
        _hideTaskbar = dock?.HideTaskbar ?? true;
        _hideIcons = dock?.HideIcons ?? false;
    }

    partial void OnDockEnabledChanged(bool value) => Save();
    partial void OnHideTaskbarChanged(bool value) => Save();
    partial void OnHideIconsChanged(bool value) => Save();

    private void Save()
    {
        if (_controller is null)
            return;
        var settings = _controller.Settings;
        settings.Dock.Enabled = DockEnabled;
        settings.Dock.HideTaskbar = HideTaskbar;
        settings.Dock.HideIcons = HideIcons;
        _controller.SaveSettings(settings);
    }
}
