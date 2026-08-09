using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AiDesk.App.Services;
using AiDesk.Core.Clipboard;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 搜索命令面板（双 Tab）：🔍 搜索（列表 + 详情预览，回车启动）｜📋 剪贴板（可折叠 + 分页 8 条/页）。
/// Ctrl+Alt+D 呼出搜索 Tab；Ctrl+Alt+V 呼出并直达剪贴板 Tab。
/// </summary>
public partial class SearchWidgetWindow : WidgetWindowBase
{
    private const int ClipPageSize = 8;

    /// <summary>搜索结果条目（应用 + 图标 + 所在目录）。</summary>
    public sealed record SearchResult(StartMenuApp App, ImageSource? Icon, string Dir);

    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;
    private readonly Dictionary<string, ImageSource?> _iconCache = new();
    private readonly ClipboardMonitor _clipboard = new();
    private int _clipPage;
    private bool _clipExpanded = true;

    /// <summary>点击"AI 对话"：请求呼出宠物对话（由外部订阅处理）。</summary>
    public event Action? AIRequested;

    public SearchWidgetWindow() : base(Services.WidgetKind.Search, topmost: true)
    {
        InitializeComponent();
        _startMenuApps = StartMenuAppsProvider.Scan();
        RefreshClipboard();
        StartTickerMs(1000); // 剪贴板历史每秒刷新

        // 失焦自动隐藏（点击面板外任意处即关闭，类似 Spotlight）
        Deactivated += (_, _) =>
        {
            if (IsLoaded)
                Close();
        };

        // Esc 关闭
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    protected override void OnWidgetLoaded() => SearchBox.Focus();

    protected override void OnTick() => RefreshClipboard();

    // ---- Tab 切换 ----

    /// <summary>切到搜索 Tab（Ctrl+Alt+D 呼出时）。</summary>
    public void SwitchToSearchTab() => SetTab(search: true);

    /// <summary>切到剪贴板 Tab（Ctrl+Alt+V 呼出时）。</summary>
    public void SwitchToClipboardTab()
    {
        SetTab(search: false);
        RefreshClipboard();
    }

    private void SetTab(bool search)
    {
        SearchTabBtn.IsChecked = search;
        ClipTabBtn.IsChecked = !search;
        SearchPage.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
        ClipPage.Visibility = search ? Visibility.Collapsed : Visibility.Visible;
        if (search)
            SearchBox.Focus();
    }

    private void OnSearchTabClick(object sender, RoutedEventArgs e) => SetTab(search: true);

    private void OnClipTabClick(object sender, RoutedEventArgs e)
    {
        SetTab(search: false);
        RefreshClipboard();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ---- 搜索：过滤 + 预览 ----

    private ImageSource? GetIcon(StartMenuApp app)
    {
        if (!_iconCache.TryGetValue(app.LnkPath, out var icon))
        {
            icon = IconHelper.GetFileIcon(app.LnkPath);
            _iconCache[app.LnkPath] = icon;
        }
        return icon;
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        List<SearchResult> results;
        if (string.IsNullOrEmpty(query))
        {
            results = new List<SearchResult>();
        }
        else
        {
            results = _startMenuApps
                .Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(12)
                .Select(a => new SearchResult(a, GetIcon(a), SafeDir(a.LnkPath)))
                .ToList();
        }
        ResultList.ItemsSource = results;
        UpdatePreview(results.FirstOrDefault());
    }

    private static string SafeDir(string path)
    {
        try { return Path.GetDirectoryName(path) ?? ""; }
        catch { return ""; }
    }

    private void OnResultSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is SearchResult r)
            UpdatePreview(r);
    }

    private void UpdatePreview(SearchResult? r)
    {
        if (r is null)
        {
            PreviewEmpty.Visibility = Visibility.Visible;
            PreviewDetail.Visibility = Visibility.Collapsed;
            return;
        }
        PreviewEmpty.Visibility = Visibility.Collapsed;
        PreviewDetail.Visibility = Visibility.Visible;
        PreviewIcon.Source = r.Icon;
        PreviewName.Text = r.App.Name;
        PreviewPath.Text = r.App.LnkPath;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var r = ResultList.SelectedItem as SearchResult
                    ?? ResultList.Items.Cast<SearchResult>().FirstOrDefault();
            if (r is not null)
                LaunchApp(r.App);
        }
    }

    private void OnResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultList.SelectedItem is SearchResult r)
            LaunchApp(r.App);
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultList.SelectedItem is SearchResult r)
            LaunchApp(r.App);
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

    // ---- 剪贴板：折叠 + 分页 ----

    private void OnFoldClick(object sender, RoutedEventArgs e)
    {
        _clipExpanded = !_clipExpanded;
        ClipFoldArea.Visibility = _clipExpanded ? Visibility.Visible : Visibility.Collapsed;
        FoldBtn.Content = _clipExpanded ? "▾" : "▸";
    }

    private void RefreshClipboard()
    {
        var items = _clipboard.History;
        Dispatcher.Invoke(() =>
        {
            ClipCount.Text = $"共 {items.Count} 条";
            var totalPages = Math.Max(1, (items.Count + ClipPageSize - 1) / ClipPageSize);
            if (_clipPage >= totalPages)
                _clipPage = totalPages - 1;
            if (_clipPage < 0)
                _clipPage = 0;

            ClipList.ItemsSource = items
                .Skip(_clipPage * ClipPageSize)
                .Take(ClipPageSize)
                .ToList();
            PageIndicator.Text = $"{_clipPage + 1}/{totalPages}";
            PrevPageBtn.IsEnabled = _clipPage > 0;
            NextPageBtn.IsEnabled = _clipPage < totalPages - 1;

            ClipEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void OnPrevPage(object sender, RoutedEventArgs e)
    {
        if (_clipPage > 0)
        {
            _clipPage--;
            RefreshClipboard();
        }
    }

    private void OnNextPage(object sender, RoutedEventArgs e)
    {
        var items = _clipboard.History;
        var totalPages = Math.Max(1, (items.Count + ClipPageSize - 1) / ClipPageSize);
        if (_clipPage < totalPages - 1)
        {
            _clipPage++;
            RefreshClipboard();
        }
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
