using System.IO;
using System.Windows;
using System.Windows.Threading;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
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

        Exit += (_, _) => Telemetry.Info("App", "应用退出");
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
