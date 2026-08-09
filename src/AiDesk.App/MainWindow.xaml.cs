using System.Windows;
using AiDesk.App.ViewModels;

namespace AiDesk.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += OnClosing;
    }

    /// <summary>窗口关闭时释放可释放的 ViewModel（如壁纸轮播 Timer），避免后台残留。</summary>
    private static void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm)
            return;
        foreach (var item in vm.NavItems)
        {
            if (item.ViewModel is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
