using System.Windows;
using AiDesk.App.ViewModels;

namespace AiDesk.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closed += OnClosed;
    }

    /// <summary>窗口关闭后释放可释放的 ViewModel（Closed 不可取消，避免半关闭状态）。</summary>
    private static void OnClosed(object? sender, EventArgs e)
    {
        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm)
            return;
        foreach (var item in vm.NavItems)
        {
            try
            {
                if (item.ViewModel is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
                // 单个释放失败不影响其余
            }
        }
    }
}
