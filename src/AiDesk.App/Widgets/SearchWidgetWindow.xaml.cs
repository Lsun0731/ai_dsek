using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AiDesk.App.Services;
using AiDesk.Core.Clipboard;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 搜索命令面板：应用磁贴网格 + 应用搜索 + 剪贴板分页历史 + AI 对话入口（毛玻璃深色）。
/// </summary>
public partial class SearchWidgetWindow : WidgetWindowBase
{
    private const int ClipPageSize = 8;
    private const int MaxTiles = 12;

    /// <summary>磁贴条目（应用 + 图标）。</summary>
    public sealed record AppTile(StartMenuApp App, ImageSource? Icon);

    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;
    private readonly List<AppTile> _tiles = new();
    private readonly Dictionary<string, ImageSource?> _iconCache = new();
    private readonly ClipboardMonitor _clipboard = new();
    private int _clipPage;

    /// <summary>点击"AI 对话"：请求呼出宠物对话（由外部订阅处理）。</summary>
    public event Action? AIRequested;

    public SearchWidgetWindow() : base(Services.WidgetKind.Search, topmost: true)
    {
        InitializeComponent();
        _startMenuApps = StartMenuAppsProvider.Scan();
        LoadTiles();
        RefreshClipboard();
        StartTickerMs(1000); // 剪贴板历史每秒刷新
    }

    protected override void OnWidgetLoaded() => SearchBox.Focus();

    protected override void OnTick() => RefreshClipboard();

    // ---- 磁贴 ----

    private void LoadTiles()
    {
        foreach (var app in _startMenuApps.Take(MaxTiles))
            _tiles.Add(new AppTile(app, GetIcon(app)));
        TileGrid.ItemsSource = _tiles;
    }

    private ImageSource? GetIcon(StartMenuApp app)
    {
        if (!_iconCache.TryGetValue(app.LnkPath, out var icon))
        {
            icon = IconHelper.GetFileIcon(app.LnkPath);
            _iconCache[app.LnkPath] = icon;
        }
        return icon;
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StartMenuApp app })
            LaunchApp(app);
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        var hasQuery = !string.IsNullOrEmpty(query);

        List<AppTile> shown;
        if (hasQuery)
        {
            shown = _startMenuApps
                .Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(MaxTiles)
                .Select(a => new AppTile(a, GetIcon(a)))
                .ToList();
        }
        else
        {
            shown = _tiles;
        }

        TileGrid.ItemsSource = shown;
        EmptyText.Visibility = hasQuery && shown.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // 剪贴板区仅在无搜索词时显示（避免残留）
        var clipVisible = !hasQuery;
        ClipTitle.Visibility = clipVisible ? Visibility.Visible : Visibility.Collapsed;
        ClipList.Visibility = clipVisible ? Visibility.Visible : Visibility.Collapsed;
        PrevPageBtn.Visibility = clipVisible ? Visibility.Visible : Visibility.Collapsed;
        NextPageBtn.Visibility = clipVisible ? Visibility.Visible : Visibility.Collapsed;
        PageIndicator.Visibility = clipVisible ? Visibility.Visible : Visibility.Collapsed;
        if (clipVisible)
            ClipEmpty.Visibility = _clipboard.History.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        else
            ClipEmpty.Visibility = Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TileGrid.Items.Count > 0 && TileGrid.ItemsSource is not null)
            LaunchApp(((AppTile)TileGrid.Items[0]).App);
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

    // ---- 剪贴板（分页：8 条/页，仅本次开机内存历史，上限 100） ----

    private void RefreshClipboard()
    {
        var items = _clipboard.History;
        Dispatcher.Invoke(() =>
        {
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

            // 搜索状态下剪贴板区整体隐藏，勿改空态提示
            var hasQuery = !string.IsNullOrEmpty(SearchBox.Text.Trim());
            if (!hasQuery)
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
