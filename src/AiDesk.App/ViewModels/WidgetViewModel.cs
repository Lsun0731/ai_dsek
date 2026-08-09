using AiDesk.App.Services;
using AiDesk.App.Widgets;
using AiDesk.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace AiDesk.App.ViewModels;

/// <summary>
/// 桌面小组件页 ViewModel：独立控制 系统状态 / 日期 / 天气 三个小组件。
/// </summary>
public partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<WidgetKind, WidgetWindowBase> _windows = [];

    [ObservableProperty]
    private bool _statsOpen;

    [ObservableProperty]
    private bool _dateOpen;

    [ObservableProperty]
    private bool _weatherOpen;

    [ObservableProperty]
    private bool _musicOpen;

    [ObservableProperty]
    private bool _searchOpen;

    [ObservableProperty]
    private bool _petOpen;

    [ObservableProperty]
    private string _aiBaseUrl = "https://api.openai.com/v1";

    [ObservableProperty]
    private string _aiApiKey = "";

    [ObservableProperty]
    private string _aiModel = "gpt-4o-mini";

    [ObservableProperty]
    private double _widgetOpacity = 0.9;

    [ObservableProperty]
    private string _weatherCity = "北京";

    /// <summary>启动时待恢复的打开状态（普通字段，避免直接引用 ObservableProperty 字段）。</summary>
    private readonly Dictionary<WidgetKind, bool> _savedOpen = new();

    public WidgetViewModel()
    {
        var settings = AppConfig.Load();
        // 字段直接赋值，避免构造函数触发 OnXxxChanged 无意义 Save / 提前打开窗口
        _widgetOpacity = settings.Opacity;
        _weatherCity = settings.WeatherCity;
        _savedOpen[WidgetKind.Stats] = settings.GetState(WidgetKind.Stats).IsOpen;
        _savedOpen[WidgetKind.Date] = settings.GetState(WidgetKind.Date).IsOpen;
        _savedOpen[WidgetKind.Weather] = settings.GetState(WidgetKind.Weather).IsOpen;
        _savedOpen[WidgetKind.Music] = settings.GetState(WidgetKind.Music).IsOpen;
        _savedOpen[WidgetKind.Search] = settings.GetState(WidgetKind.Search).IsOpen;
        _savedOpen[WidgetKind.Pet] = settings.GetState(WidgetKind.Pet).IsOpen;

        // AI 对话配置
        _aiBaseUrl = settings.AI.BaseUrl;
        _aiApiKey = settings.AI.ApiKey;
        _aiModel = settings.AI.Model;

        // 启动恢复：窗口在 MainWindow 完成构造后统一打开（延迟到主窗口 Loaded，避免抢焦点）
        Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded, OpenSavedWindows);
    }

    /// <summary>启动时恢复上次打开的窗口（主窗口显示后再开）。</summary>
    private void OpenSavedWindows()
    {
        foreach (var (kind, open) in _savedOpen)
        {
            if (!open)
                continue;
            SetOpenFlagSilently(kind, true); // 同步开关状态，避免 UI 标志与窗口不一致
            ToggleWidget(kind, true, FactoryFor(kind));
        }
    }

    private static Func<WidgetWindowBase> FactoryFor(WidgetKind kind) => kind switch
    {
        WidgetKind.Stats => () => new StatsWidgetWindow(),
        WidgetKind.Date => () => new DateWidgetWindow(),
        WidgetKind.Weather => () => new WeatherWidgetWindow(),
        WidgetKind.Music => () => new MusicWidgetWindow(),
        WidgetKind.Search => () => new SearchWidgetWindow(),
        WidgetKind.Pet => () => new PetWidgetWindow(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    partial void OnAiBaseUrlChanged(string value) => SaveAI();
    partial void OnAiApiKeyChanged(string value) => SaveAI();
    partial void OnAiModelChanged(string value) => SaveAI();

    private void SaveAI()
    {
        var settings = AppConfig.Load();
        settings.AI.BaseUrl = AiBaseUrl;
        settings.AI.ApiKey = AiApiKey;
        settings.AI.Model = AiModel;
        AppConfig.Save(settings);
    }

    private bool _suppressToggle;

    partial void OnStatsOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Stats, value, () => new StatsWidgetWindow());
    }

    partial void OnDateOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Date, value, () => new DateWidgetWindow());
    }

    partial void OnWeatherOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Weather, value, () => new WeatherWidgetWindow());
    }

    partial void OnMusicOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Music, value, () => new MusicWidgetWindow());
    }

    partial void OnSearchOpenChanged(bool value)
    {
        if (!_suppressToggle)
        {
            // 搜索面板的 AI 按钮 → 打开宠物对话
            if (value)
                ToggleWidgetWithAI(WidgetKind.Search, value, () => new SearchWidgetWindow());
            else
                ToggleWidget(WidgetKind.Search, false, () => new SearchWidgetWindow());
        }
    }

    partial void OnPetOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Pet, value, () => new PetWidgetWindow());
    }

    private void ToggleWidgetWithAI(WidgetKind kind, bool open, Func<WidgetWindowBase> factory)
    {
        var window = factory();
        if (window is SearchWidgetWindow search)
        {
            // 打开宠物需同步 PetOpen 标志（走正常属性路径），否则 UI 开关与字典不一致
            search.AIRequested += () =>
            {
                if (!PetOpen)
                    PetOpen = true;
            };
        }
        ToggleWidget(kind, open, factory, window);
    }

    /// <summary>热键 Ctrl+Alt+D 呼出/隐藏搜索小组件。</summary>
    public void ToggleSearch() => SearchOpen = !SearchOpen;

    /// <summary>Agent 联动：打开搜索面板并切到搜索 Tab（已开则激活）。</summary>
    public void ShowSearchPanel()
    {
        if (_windows.TryGetValue(WidgetKind.Search, out var existing) && existing is SearchWidgetWindow search)
        {
            search.Show();
            search.Activate();
            search.SwitchToSearchTab();
            return;
        }
        SearchOpen = true; // 默认就是搜索 Tab
    }

    /// <summary>热键 Ctrl+Alt+V：呼出搜索面板并直达剪贴板 Tab（窗口已开则切换并激活）。</summary>
    public void ShowClipboard()
    {
        if (_windows.TryGetValue(WidgetKind.Search, out var existing) && existing is SearchWidgetWindow search)
        {
            search.Show();
            search.Activate();
            search.SwitchToClipboardTab();
            return;
        }
        SearchOpen = true; // 触发打开（OnSearchOpenChanged → ToggleWidgetWithAI）
        if (_windows.TryGetValue(WidgetKind.Search, out var opened) && opened is SearchWidgetWindow openedSearch)
            openedSearch.SwitchToClipboardTab();
    }

    partial void OnWidgetOpacityChanged(double value)
    {
        // 局部更新：基于磁盘最新配置只改 Opacity，避免快照整体覆写其他模块写入
        var settings = AppConfig.Load();
        settings.Opacity = value;
        AppConfig.Save(settings);
        foreach (var window in _windows.Values)
            window.SetWidgetOpacity(value);
    }

    partial void OnWeatherCityChanged(string value)
    {
        var settings = AppConfig.Load();
        settings.WeatherCity = value;
        AppConfig.Save(settings);
        if (_windows.TryGetValue(WidgetKind.Weather, out var weather))
            weather.RefreshNow();
    }

    private void ToggleWidget(WidgetKind kind, bool open, Func<WidgetWindowBase> factory, WidgetWindowBase? prepared = null)
    {
        if (open)
        {
            if (_windows.ContainsKey(kind))
            {
                // 防御：不变量被破坏时直接丢弃新构造的窗口（未 Show 无 HWND，GC 回收即可；
                // 不能 Close——未订阅解绑会覆写磁盘 IsOpen 并误删在屏窗口记录）
                return;
            }
            var window = prepared ?? factory();
            window.Closed += (_, _) =>
            {
                _windows.Remove(kind);
                SetOpenFlagSilently(kind, false);
            };
            _windows[kind] = window;
            // 持久化打开状态，重启后记住已打开的组件（局部更新，不覆写其他字段）
            var settings = AppConfig.Load();
            settings.GetState(kind).IsOpen = true;
            AppConfig.Save(settings);
            window.Show();
            Telemetry.Event("Widget", $"打开 {kind}");
        }
        else if (_windows.TryGetValue(kind, out var existing))
        {
            existing.Close();
        }
    }

    /// <summary>窗口被关闭后同步开关状态（suppress 防止 OnXxxChanged 递归）。</summary>
    private void SetOpenFlagSilently(WidgetKind kind, bool value)
    {
        _suppressToggle = true;
        try
        {
            switch (kind)
            {
                case WidgetKind.Stats: StatsOpen = value; break;
                case WidgetKind.Date: DateOpen = value; break;
                case WidgetKind.Weather: WeatherOpen = value; break;
                case WidgetKind.Music: MusicOpen = value; break;
                case WidgetKind.Search: SearchOpen = value; break;
                case WidgetKind.Pet: PetOpen = value; break;
            }
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    public void Dispose()
    {
        // ToList 防止 Close 触发 Closed→Remove 导致迭代期修改集合。
        // 退出前置 PersistCloseState=false：Close 窗口让进程正常退出，但不把 IsOpen 覆写回 false，
        // 这样「重启恢复已打开的小组件」仍生效；用户手动点 ✕ 关闭才落盘关闭状态。
        foreach (var window in _windows.Values.ToList())
        {
            window.PersistCloseState = false;
            window.Close();
        }
        _windows.Clear();
        GC.SuppressFinalize(this);
    }
}
