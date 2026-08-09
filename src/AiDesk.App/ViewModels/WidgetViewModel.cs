using AiDesk.App.Services;
using AiDesk.App.Widgets;
using AiDesk.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDesk.App.ViewModels;

/// <summary>桌面小组件页 ViewModel：控制时钟/监控小组件的显示与外观。</summary>
public partial class WidgetViewModel : ObservableObject, IDisposable
{
    private SystemWidgetWindow? _window;
    private readonly WidgetSettings _settings = WidgetConfig.Load();

    [ObservableProperty]
    private bool _isWidgetOpen;

    [ObservableProperty]
    private double _widgetOpacity = 0.85;

    public WidgetViewModel()
    {
        WidgetOpacity = _settings.Opacity;
    }

    partial void OnIsWidgetOpenChanged(bool value)
    {
        if (value)
            OpenWidget();
        else
            CloseWidget();
    }

    partial void OnWidgetOpacityChanged(double value)
    {
        _settings.Opacity = value;
        WidgetConfig.Save(_settings);
        if (_window is not null)
            _window.Opacity = value;
    }

    private void OpenWidget()
    {
        if (_window is not null)
            return;
        _window = new SystemWidgetWindow { Opacity = WidgetOpacity };
        _window.Closed += (_, _) =>
        {
            _window = null;
            IsWidgetOpen = false;
        };
        _window.Show();
        Telemetry.Event("Widget", "打开小组件");
    }

    private void CloseWidget()
    {
        _window?.Close();
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
        GC.SuppressFinalize(this);
    }
}
