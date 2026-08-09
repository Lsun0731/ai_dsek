using System.Diagnostics;
using System.Globalization;
using System.Speech.Recognition;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.Widgets;

namespace AiDesk.App.Widgets;

/// <summary>
/// 桌面宠物：VPet 动漫角色（待机/走路动画），可拖动，可自由走动（上下左右随机）。
/// 纯语音对话：常驻监听唤醒词「AD AD」→ 提示听写 → AI Agent（启动应用/系统信息）→ 气泡文字 + TTS 朗读回复。
/// 点击宠物 = 直接开始一次听写。文字聊天已移至搜索面板的「AI 对话」Tab。
/// 动画素材来源：VPet 开源桌宠（https://github.com/LorisYounger/VPet），非商用免费。
/// </summary>
public partial class PetWidgetWindow : WidgetWindowBase
{
    private enum PetState { Idle, Walking }

    /// <summary>语音对话阶段。</summary>
    private enum SpeechPhase : int
    {
        None,      // 空闲/处理中（不监听）
        Wake,      // 监听唤醒词 AD AD
        Dictate,   // 听写中
    }

    private readonly ChatSessionService _chat = new("pet");
    private readonly List<ImageSource> _idleFrames = new();
    private readonly List<ImageSource> _walkFrames = new();
    private readonly Random _rnd = new();
    private readonly SystemStatsProvider _stats = new();
    private int _frameIndex;
    private PetState _state = PetState.Idle;
    private int _walkDir = 1;          // 1 向右 / -1 向左（动画朝向）
    private double _targetX, _targetY; // 随机目标点（自由移动）
    private DateTime _nextIdleEnd = DateTime.MinValue;

    // 语音对话（SAPI 唤醒词监听 + 听写；TTS 朗读走 PetTtsService）
    private volatile SpeechPhase _phase = SpeechPhase.None;
    private SpeechRecognitionEngine? _currentEngine;
    private bool _speechAvailable = true;

    // 对话窗口（唤醒后保持连续对话，免重复唤醒词）
    private bool _inConversation;
    private DateTime _lastActivity = DateTime.MinValue;
    private const int ConversationTimeoutSec = 45;

    // 主动行为（异常提醒 / 空闲问候，节流）
    private DateTime _lastProactiveCheck = DateTime.MinValue;
    private DateTime _lastProactiveRemind = DateTime.MinValue;

    /// <summary>对话退出词（说这些结束对话回待命）。</summary>
    private static readonly string[] ExitWords =
    {
        "再见", "拜拜", "结束", "没事了", "没有了", "不说了", "先这样", "退出", "没别的事了",
    };

    // 拖动检测（按下后轮询位移，超过阈值转系统 DragMove —— 丝滑）
    private DispatcherTimer? _dragDetect;
    private bool _dragStarted;

    /// <summary>唤醒词候选（SAPI 中文引擎对英文发音的多种识别结果）。</summary>
    private static readonly string[] WakeWords = { "AD AD", "阿迪阿迪", "艾迪艾迪", "诶低诶低" };

    public PetWidgetWindow() : base(Services.WidgetKind.Pet, topmost: true)
    {
        InitializeComponent();
        LoadFrames();
        StartTickerMs(125); // 帧时长 125ms

        Loaded += (_, _) => StartWakeListen();
    }

    /// <summary>宠物禁用基类 DragMove（点击=听写，拖动用位移检测后手动调用 DragMove）。</summary>
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

        CheckProactive(); // 主动行为（异常提醒，节流）
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

        if (Math.Abs(Left - _targetX) < step && Math.Abs(Top - _targetY) < step)
        {
            _state = PetState.Idle;
            _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(8, 18));
            return;
        }

        var dx = _targetX - Left;
        var dy = _targetY - Top;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
        {
            _state = PetState.Idle;
            _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(8, 18));
            return;
        }

        if (Math.Abs(dx) > 1)
        {
            var newDir = dx > 0 ? 1 : -1;
            if (newDir != _walkDir)
                FlipDirection(newDir);
        }

        Left += dx / len * step;
        Top += dy / len * step;

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

    // ---- 语音对话状态机（唤醒词 AD AD → 对话窗口 → AI → TTS 回复） ----

    /// <summary>启动唤醒词监听（常驻待命）。</summary>
    private void StartWakeListen()
    {
        if (!_speechAvailable || !IsLoaded)
            return;
        _inConversation = false;
        _phase = SpeechPhase.Wake;
        var grammar = new Grammar(new Choices(WakeWords));
        RunRecognition(grammar, text =>
        {
            _phase = SpeechPhase.None; // 命中唤醒：暂停监听，走听写
            _inConversation = true;    // 进入对话窗口（免重复唤醒词）
            _lastActivity = DateTime.Now;
            Dispatcher.Invoke(() =>
            {
                Bubble.Visibility = Visibility.Visible;
                BubbleText.Text = "👂 我在，请说…";
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { /* 提示音可选 */ }
            });
            StartDictation();
        });
    }

    /// <summary>开始一次听写（说一句话，说完自动识别）。</summary>
    public void StartDictation()
    {
        if (!_speechAvailable || !IsLoaded)
            return;
        _phase = SpeechPhase.Dictate;
        Dispatcher.Invoke(() =>
        {
            Bubble.Visibility = Visibility.Visible;
            BubbleText.Text = _inConversation ? "🎤 请说…（再见 结束对话）" : "🎤 听写中…（说完自动发送）";
        });
        var grammar = new DictationGrammar();
        RunRecognition(grammar, text =>
        {
            _phase = SpeechPhase.None; // 已拿到结果：暂停监听
            Dispatcher.InvokeAsync(async () => await HandleDictationAsync(text));
        });
    }

    /// <summary>听写结果 → AI Agent（多轮记忆）→ 气泡 + TTS 朗读 → 对话窗口内继续听。</summary>
    private async Task HandleDictationAsync(string text)
    {
        if (!IsLoaded)
            return;
        text = text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            BubbleText.Text = "没听清，再说一次？";
            if (_inConversation)
                StartDictation();
            else
                StartWakeListen();
            return;
        }

        // 退出词：结束对话回待命
        if (_inConversation && ExitWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            _inConversation = false;
            _chat.Clear();
            BubbleText.Text = "好的，需要时叫我～";
            await PetTtsService.SpeakAsync("好的，需要时叫我");
            StartWakeListen();
            return;
        }

        _lastActivity = DateTime.Now;
        BubbleText.Text = "🧠 思考中…";

        var content = await _chat.SendAsync(text, reply =>
        {
            // 窗口可能已关闭：防止回写已释放控件
            if (!IsLoaded)
                return;
            BubbleText.Text = reply; // 最终完整回复（净化后）
        }, "Pet.Chat",
        onDelta: chunk =>
        {
            if (!IsLoaded)
                return;
            if (BubbleText.Text == "🧠 思考中…" || BubbleText.Text.StartsWith("（正在"))
                BubbleText.Text = "";
            BubbleText.Text += chunk; // 流式实时显示
        },
        onToolRunning: (name, _) =>
        {
            if (!IsLoaded)
                return;
            BubbleText.Text = $"（正在执行 {name}…）";
        });

        if (!IsLoaded)
            return;

        // 成功回复 → 语音朗读（错误文本已在回调中展示，不朗读）
        if (content is not null)
            await PetTtsService.SpeakAsync(content);

        // 对话窗口内：继续听下一条；否则回待命
        if (_inConversation)
            StartDictation();
        else
            StartWakeListen();
    }

    /// <summary>运行一次识别（引擎单次使用，完成后按阶段重启监听）。</summary>
    private void RunRecognition(Grammar grammar, Action<string> onResult)
    {
        SpeechRecognitionEngine? engine = null;
        try
        {
            _currentEngine?.Dispose();
            engine = new SpeechRecognitionEngine(new CultureInfo("zh-CN"));
            _currentEngine = engine;
            engine.LoadGrammar(grammar);
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, e) =>
            {
                var text = e.Result?.Text ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                    onResult(text);
            };
            engine.RecognizeCompleted += OnRecognizeCompleted;
            engine.RecognizeAsync(RecognizeMode.Single);
        }
        catch (Exception ex)
        {
            try { engine?.Dispose(); } catch { /* 忽略 */ }
            _speechAvailable = false;
            _phase = SpeechPhase.None;
            Telemetry.Error("Pet.Speech", ex);
            Dispatcher.Invoke(() =>
            {
                if (!IsLoaded)
                    return;
                Bubble.Visibility = Visibility.Visible;
                BubbleText.Text = $"语音不可用：{ex.Message}";
            });
        }
    }

    /// <summary>识别结束（超时/无结果/取消）：对话窗口静默超时退出，否则按阶段继续。</summary>
    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Cancelled)
            return;
        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded)
                return; // 窗口已关闭：不再重启监听（防引擎泄漏）
            switch (_phase)
            {
                case SpeechPhase.Wake:
                    StartWakeListen();
                    break;
                case SpeechPhase.Dictate:
                    // 对话窗口静默超时 → 退出对话回待命
                    if (_inConversation && DateTime.Now - _lastActivity > TimeSpan.FromSeconds(ConversationTimeoutSec))
                    {
                        _inConversation = false;
                        _chat.Clear();
                        BubbleText.Text = "那我先待命啦，说 AD AD 找我～";
                        StartWakeListen();
                    }
                    else
                    {
                        StartDictation(); // 没听到有效内容 → 重新听
                    }
                    break;
            }
        });
    }

    // ---- 主动行为（异常提醒 / 空闲问候，节流） ----

    private void CheckProactive()
    {
        if (_phase != SpeechPhase.None || _inConversation)
            return;
        var now = DateTime.Now;
        if (now - _lastProactiveCheck < TimeSpan.FromMinutes(5))
            return;
        _lastProactiveCheck = now;
        if (now - _lastProactiveRemind < TimeSpan.FromMinutes(30))
            return;

        try
        {
            var stats = _stats.Sample();
            var warning = new List<string>();
            foreach (var disk in stats.Disks)
            {
                if (disk.Percent >= 90)
                    warning.Add($"磁盘 {disk.Name} 已用 {disk.Percent:F0}%");
            }
            if (stats.MemPercent >= 90)
                warning.Add($"内存占用 {stats.MemPercent:F0}%");
            if (stats.CpuPercent >= 95)
                warning.Add($"CPU 占用 {stats.CpuPercent:F0}%");

            if (warning.Count > 0)
            {
                _lastProactiveRemind = now;
                var msg = "提醒：" + string.Join("；", warning) + "。需要我帮你清理吗？";
                Dispatcher.Invoke(() =>
                {
                    Bubble.Visibility = Visibility.Visible;
                    BubbleText.Text = msg;
                });
                _ = PetTtsService.SpeakAsync(msg);
                Telemetry.Event("Pet", "主动提醒");
            }
        }
        catch (Exception ex)
        {
            Telemetry.Error("Pet.Proactive", ex);
        }
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
        {
            _inConversation = true; // 点击宠物 = 进入对话窗口并听写（等效唤醒词）
            _lastActivity = DateTime.Now;
            StartDictation();
        }
        base.OnMouseLeftButtonUp(e);
    }

    // ---- Agent 工具执行（公共 AgentTools） ----

    protected override void OnClosed(System.EventArgs e)
    {
        _dragDetect?.Stop();
        try
        {
            _phase = SpeechPhase.None;
            _currentEngine?.RecognizeAsyncCancel();
            _currentEngine?.Dispose();
        }
        catch
        {
            // 释放语音资源失败不影响退出
        }
        _stats.Dispose(); // PerformanceCounter 句柄必须释放（无终结器）
        _chat.Dispose();
        base.OnClosed(e);
    }
}
