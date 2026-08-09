using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.AI;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.Widgets;
using Windows.Media.SpeechRecognition;

namespace AiDesk.App.Widgets;

/// <summary>
/// 桌面宠物：VPet 动漫角色（待机/走路动画），可拖动，可自由走动（上下左右随机），
/// 点击呼出输入框：文字 / 语音（Windows 听写）→ AI Agent（可启动应用、查询系统信息）→ 头顶气泡回复。
/// 动画素材来源：VPet 开源桌宠（https://github.com/LorisYounger/VPet），非商用免费。
/// </summary>
public partial class PetWidgetWindow : WidgetWindowBase
{
    private enum PetState { Idle, Walking }

    private readonly AIChatClient _ai = new();
    private readonly List<ImageSource> _idleFrames = new();
    private readonly List<ImageSource> _walkFrames = new();
    private readonly Random _rnd = new();
    private readonly SystemStatsProvider _stats = new();
    private int _frameIndex;
    private PetState _state = PetState.Idle;
    private int _walkDir = 1;          // 1 向右 / -1 向左（动画朝向）
    private double _targetX, _targetY; // 随机目标点（自由移动）
    private DateTime _nextIdleEnd = DateTime.MinValue;
    private bool _chatMode;
    private bool _thinking;

    // 语音识别（Windows 听写，免费离线）
    private SpeechRecognizer? _recognizer;
    private bool _listening;

    // 拖动检测（按下后轮询位移，超过阈值转系统 DragMove —— 丝滑）
    private DispatcherTimer? _dragDetect;
    private bool _dragStarted;

    /// <summary>Agent 工具列表（启动应用 / 系统信息）。</summary>
    private static readonly AITool[] Tools =
    {
        new()
        {
            Name = "launch_app",
            Description = "启动一个已安装的应用程序（按名称匹配，如：记事本、计算器、Chrome、设置）。",
            ParametersJsonSchema = """
                {"type":"object","properties":{"name":{"type":"string","description":"应用名称关键词"}},"required":["name"]}
                """,
        },
        new()
        {
            Name = "get_system_info",
            Description = "查询电脑系统信息：操作系统版本、CPU 使用率、内存、磁盘占用。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
    };

    public PetWidgetWindow() : base(Services.WidgetKind.Pet, topmost: true)
    {
        InitializeComponent();
        LoadFrames();
        StartTickerMs(125); // 帧时长 125ms
        ShowGreeting();
    }

    /// <summary>宠物禁用基类 DragMove（点击=聊天，拖动用位移检测后手动调用 DragMove）。</summary>
    protected override bool ShouldDrag(System.Windows.Input.MouseButtonEventArgs e) => false;

    private void LoadFrames()
    {
        try
        {
            for (var i = 0; i < 17; i++)
                _idleFrames.Add(LoadFrame($"Assets/Pet/pet_{i:D3}.png"));
            for (var i = 0; i < 3; i++)
                _walkFrames.Add(LoadFrame($"Assets/Pet/walk_{i:D3}.png"));

            if (_idleFrames.Count > 0)
                PetImage.Source = _idleFrames[0];
        }
        catch (Exception ex)
        {
            Telemetry.Error("Pet.LoadFrames", ex);
        }
    }

    private static ImageSource LoadFrame(string relativeUri)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/{relativeUri}");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnTick()
    {
        if (_idleFrames.Count == 0)
            return;

        // 用户按住鼠标（拖动/点击中）时暂停走动
        var userInteracting = Mouse.LeftButton == MouseButtonState.Pressed || _dragStarted;
        if (userInteracting && _state == PetState.Walking)
        {
            _state = PetState.Idle;
            _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(6, 14));
        }

        if (_state == PetState.Walking && _walkFrames.Count > 0)
        {
            TickWalk();
            PetImage.Source = _walkFrames[_frameIndex % _walkFrames.Count];
        }
        else
        {
            DecideNextAction();
            PetImage.Source = _idleFrames[_frameIndex % _idleFrames.Count];
        }
        _frameIndex++;
    }

    /// <summary>走动一步：沿向量向随机目标点移动，四边碰壁折返。</summary>
    private void TickWalk()
    {
        const double step = 3.0; // 每帧移动 3 DIP（~24 DIP/s）

        var work = SystemParameters.WorkArea;
        var minX = work.Left;
        var minY = work.Top;
        var maxX = work.Right - ActualWidth;
        var maxY = work.Bottom - ActualHeight;

        // 到达目标 → 待机
        if (Math.Abs(Left - _targetX) < step && Math.Abs(Top - _targetY) < step)
        {
            _state = PetState.Idle;
            _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(8, 18));
            return;
        }

        // 计算朝向目标的方向向量
        var dx = _targetX - Left;
        var dy = _targetY - Top;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
        {
            _state = PetState.Idle;
            _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(8, 18));
            return;
        }

        // 水平朝向（决定动画翻转）
        if (Math.Abs(dx) > 1)
        {
            var newDir = dx > 0 ? 1 : -1;
            if (newDir != _walkDir)
                FlipDirection(newDir);
        }

        // 移动
        Left += dx / len * step;
        Top += dy / len * step;

        // 碰壁：重新选目标让角色折返
        var hitX = Left <= minX || Left >= maxX;
        var hitY = Top <= minY || Top >= maxY;
        if (hitX || hitY)
        {
            _targetX = hitX
                ? (Left <= minX ? minX + _rnd.Next(80, 400) : maxX - _rnd.Next(80, 400))
                : _rnd.Next((int)minX, (int)Math.Max(minX + 1, maxX));
            _targetY = hitY
                ? (Top <= minY ? minY + _rnd.Next(40, 200) : maxY - _rnd.Next(40, 200))
                : _rnd.Next((int)minY, (int)Math.Max(minY + 1, maxY));
            _targetX = Math.Clamp(_targetX, minX, Math.Max(minX, maxX));
            _targetY = Math.Clamp(_targetY, minY, Math.Max(minY, maxY));
            if (hitX)
                FlipDirection(Left <= minX ? 1 : -1);
        }
    }

    private void FlipDirection(int dir)
    {
        _walkDir = dir;
        PetImage.RenderTransformOrigin = new Point(0.5, 0.5);
        PetImage.RenderTransform = new ScaleTransform(_walkDir, 1);
    }

    /// <summary>待机状态随机决策：概率触发一次自由走动（随机目标点，上下左右任意）。</summary>
    private void DecideNextAction()
    {
        if (DateTime.Now < _nextIdleEnd)
            return;
        if (_rnd.NextDouble() < 0.65)
        {
            var work = SystemParameters.WorkArea;
            var minX = Math.Max(0, work.Left);
            var minY = Math.Max(0, work.Top);
            var maxX = Math.Max(minX + 1, work.Right - ActualWidth);
            var maxY = Math.Max(minY + 1, work.Bottom - ActualHeight);
            _targetX = _rnd.Next((int)minX, (int)maxX + 1);
            _targetY = _rnd.Next((int)minY, (int)maxY + 1);
            FlipDirection(_targetX >= Left ? 1 : -1);
            _state = PetState.Walking;
        }
        _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(8, 18));
    }

    private void ShowGreeting()
    {
        Bubble.Visibility = Visibility.Visible;
        BubbleText.Text = "你好呀！点我聊天～";
    }

    // ---- 拖动（丝滑：位移检测 → 系统 DragMove） ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStarted = false;
        var down = e.GetPosition(null);
        _dragDetect?.Stop();
        _dragDetect = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _dragDetect.Tick += (_, _) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                _dragDetect.Stop();
                return;
            }
            var cur = Mouse.GetPosition(null);
            if (Math.Abs(cur.X - down.X) > 5 || Math.Abs(cur.Y - down.Y) > 5)
            {
                _dragDetect.Stop();
                _dragStarted = true;
                try { DragMove(); } catch { /* 快速释放等异常忽略 */ }
                _dragStarted = false;
            }
        };
        _dragDetect.Start();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragDetect?.Stop();
        if (!_dragStarted)
            ToggleChat();
        base.OnMouseLeftButtonUp(e);
    }

    private void ToggleChat()
    {
        if (_thinking)
            return;
        _chatMode = !_chatMode;
        InputPanel.Visibility = _chatMode ? Visibility.Visible : Visibility.Collapsed;
        BubbleText.Visibility = _chatMode ? Visibility.Collapsed : Visibility.Visible;
        Bubble.Visibility = Visibility.Visible;
        if (_chatMode)
            InputBox.Focus();
        else
            ShowGreeting();
    }

    // ---- 语音输入（Windows 听写） ----

    private async void OnMicClicked(object sender, RoutedEventArgs e)
    {
        if (_listening)
            await StopListeningAsync();
        else
            await StartListeningAsync();
    }

    private async Task StartListeningAsync()
    {
        try
        {
            _recognizer ??= new SpeechRecognizer();
            if (_recognizer.Constraints.Count == 0)
            {
                _recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation, "dictation"));
            }
            await _recognizer.CompileConstraintsAsync();
            _recognizer.ContinuousRecognitionSession.ResultGenerated += OnSpeechResult;
            await _recognizer.ContinuousRecognitionSession.StartAsync();
            _listening = true;
            BubbleText.Text = "🎤 听写中…（说完点 ⏹ 发送）";
            BubbleText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            BubbleText.Text = $"语音不可用：{ex.Message}";
            Telemetry.Error("Pet.Speech", ex);
        }
    }

    private void OnSpeechResult(SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var text = args.Result.Text;
        Dispatcher.Invoke(() =>
        {
            InputBox.Text = text;
            if (args.Result.Confidence > SpeechRecognitionConfidence.Low)
            {
                BubbleText.Text = $"听写：{text}";
                BubbleText.Visibility = Visibility.Visible;
            }
        });
    }

    private async Task StopListeningAsync()
    {
        if (_recognizer is null)
            return;
        try
        {
            await _recognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch
        {
            // 忽略
        }
        if (_recognizer is not null)
            _recognizer.ContinuousRecognitionSession.ResultGenerated -= OnSpeechResult;
        _listening = false;

        // 听写结束自动发送
        var text = InputBox.Text.Trim();
        if (!string.IsNullOrEmpty(text))
            await SendAsync();
    }

    // ---- AI Agent（工具调用：启动应用 / 系统信息） ----

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = SendAsync();
    }

    private async void OnSendClicked(object sender, RoutedEventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        var message = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(message) || _thinking)
            return;

        var settings = AppConfig.Load().AI;
        _thinking = true;
        _chatMode = false;
        InputPanel.Visibility = Visibility.Collapsed;
        BubbleText.Visibility = Visibility.Visible;
        BubbleText.Text = "思考中…";

        var reply = await _ai.ChatWithToolsAsync(settings, message, Tools, ExecuteTool);

        // await 期间窗口可能已关闭
        if (!IsLoaded)
            return;

        _thinking = false;

        if (reply.IsError)
        {
            BubbleText.Text = reply.Error ?? "出错了";
            Telemetry.Function("Pet.Chat", false, 0, $"err={reply.Error}");
        }
        else
        {
            BubbleText.Text = reply.Content ?? "";
            Telemetry.Function("Pet.Chat", true, 0, $"len={reply.Content?.Length}");
        }
    }

    /// <summary>执行模型请求的工具调用，返回工具结果文本。</summary>
    private string ExecuteTool(string name, string argumentsJson)
    {
        try
        {
            switch (name)
            {
                case "launch_app":
                    return ExecuteLaunchApp(argumentsJson);
                case "get_system_info":
                    return GetSystemInfo();
                default:
                    return $"未知工具: {name}";
            }
        }
        catch (Exception ex)
        {
            return $"工具执行失败: {ex.Message}";
        }
    }

    private string ExecuteLaunchApp(string argumentsJson)
    {
        string keyword;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            keyword = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        }
        catch
        {
            keyword = "";
        }
        if (string.IsNullOrWhiteSpace(keyword))
            return "缺少应用名称参数";

        var app = StartMenuAppsProvider.Scan()
            .FirstOrDefault(a => a.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        if (app is null)
            return $"未找到应用「{keyword}」。可用名称如：记事本、计算器、设置、画图、截图工具。";

        Process.Start(new ProcessStartInfo(app.LnkPath) { UseShellExecute = true });
        return $"已启动应用「{app.Name}」";
    }

    private string GetSystemInfo()
    {
        var stats = _stats.Sample();
        var os = Environment.OSVersion.VersionString;
        var cpu = $"{stats.CpuPercent:F0}%";
        var mem = $"{stats.MemPercent:F0}% 已用（{stats.MemUsedGb:F1}/{stats.MemTotalGb:F1} GB）";
        var disks = string.Join("；", stats.Disks.Select(d => $"{d.Name} {d.Percent:F0}% 已用"));
        return $"操作系统: {os}；CPU 使用率: {cpu}；内存: {mem}；磁盘: {disks}";
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _dragDetect?.Stop();
        _recognizer?.Dispose();
        _stats.Dispose();
        _ai.Dispose();
        base.OnClosed(e);
    }
}
