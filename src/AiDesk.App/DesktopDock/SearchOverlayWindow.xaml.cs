using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.DesktopDock;

/// <summary>应用搜索浮层：输入即搜开始菜单应用，回车/双击启动，Esc/失焦关闭。</summary>
public partial class SearchOverlayWindow : Window
{
    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;

    public SearchOverlayWindow()
    {
        InitializeComponent();
        _startMenuApps = StartMenuAppsProvider.Scan();

        // 位置：屏幕顶部居中
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 48;

        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            // 兜底抢前台，避免 Deactivated→Close 闪退
            NativeMethods.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        };
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        List<StartMenuApp> results;
        if (string.IsNullOrEmpty(query))
            results = [];
        else
            results = _startMenuApps
                .Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(10)
                .ToList();

        SearchList.ItemsSource = results;
        SearchList.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = !string.IsNullOrEmpty(query) && results.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchList.Items.Count > 0 && SearchList.ItemsSource is not null)
            LaunchApp((StartMenuApp)SearchList.Items[0]);
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is StartMenuApp app)
            LaunchApp(app);
    }

    private void LaunchApp(StartMenuApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.LnkPath) { UseShellExecute = true });
            Telemetry.Function("Dock.Search.Launch", true, 0, $"app={app.Name}");
            Close();
        }
        catch (Exception ex)
        {
            Telemetry.Function("Dock.Search.Launch", false, 0, $"app={app.Name} err={ex.Message}");
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Close();
}
