using System.IO;
using System.Windows;
using System.Windows.Threading;
using AiDesk.App.DesktopDock;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private TrayIconService? _tray;
    private HotKeyService? _hotKey;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 生命周期埋点
        Telemetry.Info("App", $"应用启动 v{Environment.Version} OS={Environment.OSVersion.VersionString}");

        // 全局异常兜底：记录到埋点日志，UI 线程异常弹友好提示而非静默退出
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Telemetry.Error("AppDomain", args.ExceptionObject as Exception ?? new Exception("未知 AppDomain 异常"));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Telemetry.Error("Task", args.Exception);
            args.SetObserved();
        };

        // 桌面集成（Dock + 任务栏/图标开关），按配置立即应用
        DesktopIntegrationController.Initialize();

        // 全局热键 Ctrl+Alt+D：呼出应用搜索浮层
        _hotKey = new HotKeyService(1);
        if (!_hotKey.Register(0x2 | 0x1, 0x44)) // MOD_CONTROL|MOD_ALT, VK_D
            Telemetry.Info("App", "全局热键 Ctrl+Alt+D 注册失败（可能被占用）");
        else
            _hotKey.Pressed += () => DesktopIntegrationController.Instance?.ShowSearch();

        _tray = new TrayIconService();
        _tray.ShowSecondDesktopRequested += () => DesktopIntegrationController.Instance?.ShowSearch();
        _tray.ShowMainWindowRequested += ShowMainWindow;
        _tray.ExitRequested += () => Shutdown();

        // 主窗口（去掉 StartupUri，手动创建以便持有引用）
        _mainWindow = new MainWindow();
        _mainWindow.Show();

        Exit += (_, _) => Telemetry.Info("App", "应用退出");
        Exit += (_, _) => Cleanup();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;
        _mainWindow.Show();
        _mainWindow.Activate();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
    }

    private void Cleanup()
    {
        // 恢复任务栏/图标（explorer 独立进程，隐藏状态会残留）
        DesktopIntegrationController.Instance?.Cleanup();
        _hotKey?.Dispose();
        _tray?.Dispose();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Telemetry.Error("Dispatcher", e.Exception);
        MessageBox.Show(
            $"程序发生未处理的错误：\n\n{e.Exception.Message}\n\n详细信息已写入：\n{Telemetry.LogDirectory}",
            "AiDesk 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
