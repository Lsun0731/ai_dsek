using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AiDesk.App.Services;
using AiDesk.Core.Clipboard;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 搜索命令面板：应用搜索 + 剪贴板历史 + AI 对话入口（Raycast 风格）。
/// </summary>
public partial class SearchWidgetWindow : WidgetWindowBase
{
    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;
    private readonly ClipboardMonitor _clipboard = new();

    /// <summary>点击"AI 对话"：请求呼出宠物对话（由外部订阅处理）。</summary>
    public event Action? AIRequested;

    public SearchWidgetWindow() : base(Services.WidgetKind.Search, topmost: true)
    {
        InitializeComponent();
        _startMenuApps = StartMenuAppsProvider.Scan();
        RefreshClipboard();
    }

    protected override void OnWidgetLoaded() => SearchBox.Focus();

    protected override void OnTick() => RefreshClipboard();

    // ---- 应用搜索 ----

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
        SearchList.Visibility = !string.IsNullOrEmpty(query)
            ? Visibility.Visible : Visibility.Collapsed;
        ClipTitle.Visibility = string.IsNullOrEmpty(query)
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
            Telemetry.Function("Search.Launch", true, 0, $"app={app.Name}");
            Close();
        }
        catch (Exception ex)
        {
            Telemetry.Function("Search.Launch", false, 0, $"app={app.Name} err={ex.Message}");
        }
    }

    // ---- 剪贴板 ----

    private void RefreshClipboard()
    {
        var items = _clipboard.History;
        Dispatcher.Invoke(() =>
        {
            ClipList.ItemsSource = items.Take(8).ToList();
            ClipEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void OnClipButtonClick(object sender, RoutedEventArgs e)
    {
        // 展开/收起剪贴板区
        ClipList.Visibility = ClipList.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        ClipTitle.Visibility = ClipList.Visibility;
    }

    private void OnClipItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string text })
        {
            _clipboard.CopyToClipboard(text);
            Telemetry.Function("Search.Clipboard", true, 0, $"len={text.Length}");
        }
    }

    // ---- AI 对话 ----

    private void OnAIButtonClick(object sender, RoutedEventArgs e)
    {
        AIRequested?.Invoke();
        Telemetry.Event("Search", "请求 AI 对话");
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _clipboard.Dispose();
        base.OnClosed(e);
    }
}
