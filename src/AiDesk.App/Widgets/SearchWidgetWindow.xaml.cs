using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AiDesk.App.Services;
using AiDesk.Core.AI;
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

    /// <summary>聊天消息（AI 对话 Tab，可变文本支持流式追加）。</summary>
    public sealed class ChatMessage : System.ComponentModel.INotifyPropertyChanged
    {
        public string Role { get; init; } = "";
        private string _text = "";

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Text)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;
    private readonly Dictionary<string, ImageSource?> _iconCache = new();
    private readonly ClipboardMonitor _clipboard = new();
    private readonly ChatSessionService _chat = new("search");
    private int _clipPage;
    private bool _clipExpanded = true;

    /// <summary>点击"AI 对话"：请求呼出宠物对话（由外部订阅处理）。</summary>
    public event Action? AIRequested;

    public SearchWidgetWindow() : base(Services.WidgetKind.Search, topmost: true)
    {
        InitializeComponent();
        _startMenuApps = StartMenuAppsProvider.Scan();
        RefreshClipboard();
        VoiceBox.ItemsSource = GetGroupedVoices();
        VoiceBox.SelectedValue = AppConfig.Load().AI.Voice;
        InitPermissionBoxes();
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
        SetTab(clipboard: true);
        RefreshClipboard();
    }

    private void SetTab(bool search = false, bool clipboard = false)
    {
        SearchTabBtn.IsChecked = search;
        ClipTabBtn.IsChecked = clipboard;
        AITabBtn.IsChecked = !search && !clipboard;
        SearchPage.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
        ClipPage.Visibility = clipboard ? Visibility.Visible : Visibility.Collapsed;
        AIPage.Visibility = !search && !clipboard ? Visibility.Visible : Visibility.Collapsed;
        if (search)
            SearchBox.Focus();
        else if (!clipboard)
            ChatInput.Focus();
    }

    private void OnSearchTabClick(object sender, RoutedEventArgs e) => SetTab(search: true);

    private void OnClipTabClick(object sender, RoutedEventArgs e)
    {
        SetTab(clipboard: true);
        RefreshClipboard();
    }

    private void OnAITabClick(object sender, RoutedEventArgs e) => SetTab();

    /// <summary>音色列表按语言分组（中文/台湾/粤语/英语…）。</summary>
    private static System.ComponentModel.ICollectionView GetGroupedVoices()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(PetTtsService.Voices);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(PetTtsService.VoiceOption.Language)));
        return view;
    }

    // ---- 工具权限设置（危险工具 allow/deny/ask） ----

    private static readonly (string Value, string Label)[] PermOptions =
    {
        ("ask", "询问"),
        ("allow", "允许"),
        ("deny", "拒绝"),
    };

    private void InitPermissionBoxes()
    {
        var perms = AppConfig.Load().AI.ToolPermissions;
        InitPermBox(KillPermBox, "kill_process", perms);
        InitPermBox(TempPermBox, "clear_temp", perms);
        InitPermBox(CleanPermBox, "cleanup_computer", perms);
    }

    private static void InitPermBox(System.Windows.Controls.ComboBox box, string tool, Dictionary<string, string> perms)
    {
        box.ItemsSource = PermOptions;
        box.DisplayMemberPath = "Label";
        box.SelectedValuePath = "Value";
        box.SelectedValue = perms.TryGetValue(tool, out var rule) ? rule : "ask";
    }

    private void OnPermChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox { Tag: string tool } box ||
            box.SelectedValue is not string rule)
            return;
        var settings = AppConfig.Load();
        settings.AI.ToolPermissions[tool] = rule;
        AppConfig.Save(settings);
        Telemetry.Event("Search", $"权限 {tool}={rule}");
    }

    // ---- 宠物音色设置 ----

    private void OnVoiceChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (VoiceBox.SelectedValue is not string voice || voice.Length == 0)
            return;
        var settings = AppConfig.Load();
        settings.AI.Voice = voice;
        AppConfig.Save(settings);
        Telemetry.Event("Search", $"切换音色 {voice}");
    }

    private async void OnVoicePreviewClick(object sender, RoutedEventArgs e)
    {
        if (VoiceBox.SelectedValue is not string voice || voice.Length == 0)
            return;
        VoiceStatus.Text = "播放中…";
        var usedEdge = await PetTtsService.SpeakAsync("你好，我是你的桌面助手，你觉得这个声音怎么样？");
        if (voice.StartsWith("edge:"))
            VoiceStatus.Text = usedEdge
                ? "edge 在线音色"
                : "已回退系统语音（edge 不可用，检查网络或 python）";
        else
            VoiceStatus.Text = "系统语音（离线）";
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

    // ---- AI 对话（文字聊天 + Agent 工具，多轮记忆） ----

    private void OnChatKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = SendChatAsync();
    }

    private async void OnChatSendClicked(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async Task SendChatAsync()
    {
        var message = ChatInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(message) || _chat.IsBusy)
            return;

        ChatInput.Clear();
        var userMsg = new ChatMessage { Role = "user", Text = message };
        ChatList.Items.Add(userMsg);
        var assistantMsg = new ChatMessage { Role = "assistant" };
        ChatList.Items.Add(assistantMsg);
        ChatScroll.ScrollToEnd();

        await _chat.SendAsync(message, content =>
        {
            // 窗口可能已在请求期间关闭（_chat.Dispose 已执行），防止回写已释放控件
            if (!IsLoaded)
                return;
            assistantMsg.Text = content;
            ChatScroll.ScrollToEnd();
        }, "Search.Chat",
        onDelta: chunk =>
        {
            if (!IsLoaded)
                return;
            assistantMsg.Text += chunk; // 流式追加，实时显示
        },
        onToolRunning: (name, _) =>
        {
            if (!IsLoaded)
                return;
            assistantMsg.Text = $"（正在执行 {name}…）";
        });
    }

    private void OnAIButtonClick(object sender, RoutedEventArgs e)
    {
        AIRequested?.Invoke();
        Telemetry.Event("Search", "请求 AI 对话");
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _clipboard.Dispose();
        _chat.Dispose();
        base.OnClosed(e);
    }
}
