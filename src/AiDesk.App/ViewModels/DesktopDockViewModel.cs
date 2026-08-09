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
        var settings = _controller?.Settings;
        _dockEnabled = settings?.Enabled ?? true;
        _hideTaskbar = settings?.HideTaskbar ?? true;
        _hideIcons = settings?.HideIcons ?? false;
    }

    partial void OnDockEnabledChanged(bool value) => Save();
    partial void OnHideTaskbarChanged(bool value) => Save();
    partial void OnHideIconsChanged(bool value) => Save();

    private void Save()
    {
        _controller?.SaveSettings(new DockSettings
        {
            Enabled = DockEnabled,
            HideTaskbar = HideTaskbar,
            HideIcons = HideIcons,
        });
    }
}
