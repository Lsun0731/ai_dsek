using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
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

        // UI 风格预览模式：--preview 只显示预览窗口（供选择风格），不加载主界面
        if (e.Args.Contains("--preview"))
        {
            var preview = new StylePreviewWindow();
            preview.Show();
            preview.Closed += (_, _) => Shutdown();
            return;
        }

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

        // 首机会异常：捕获 AccessViolation 等致命异常的调用栈（AV 不触发 UnhandledException）
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (args.Exception is AccessViolationException or SEHException or NullReferenceException
                or InvalidOperationException or ArgumentException)
                Telemetry.Error("FirstChance", args.Exception);
        };

        // 全局热键：Ctrl+Alt+D 呼出搜索面板（搜索 Tab）；Ctrl+Alt+V 呼出并直达剪贴板 Tab
        _hotKey = new HotKeyService();
        if (_hotKey.Register(0x2 | 0x1, 0x44, ToggleSearchWidget) == 0) // MOD_CONTROL|MOD_ALT, VK_D
            Telemetry.Info("App", "全局热键 Ctrl+Alt+D 注册失败（可能被占用）");
        if (_hotKey.Register(0x2 | 0x1, 0x56, ShowClipboardWidget) == 0) // MOD_CONTROL|MOD_ALT, VK_V
            Telemetry.Info("App", "全局热键 Ctrl+Alt+V 注册失败（可能被占用）");

        _tray = new TrayIconService();
        _tray.ShowSecondDesktopRequested += ToggleSearchWidget;
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

    /// <summary>Agent 联动入口：显示主窗口。</summary>
    public void ShowMainWindowFromAgent() => ShowMainWindow();

    /// <summary>热键/托盘呼出搜索小组件。</summary>
    private void ToggleSearchWidget()
    {
        if (_mainWindow?.DataContext is MainViewModel vm)
            vm.ToggleSearchWidget();
    }

    /// <summary>热键 Ctrl+Alt+V：呼出搜索面板并直达剪贴板 Tab。</summary>
    private void ShowClipboardWidget()
    {
        if (_mainWindow?.DataContext is MainViewModel vm)
            vm.ShowClipboard();
    }

    private void Cleanup()
    {
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
