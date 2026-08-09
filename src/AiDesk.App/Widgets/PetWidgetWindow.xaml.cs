using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        Interrupt, // 朗读回复中，监听打断词（停止/别说了…）
    }

    private readonly ChatSessionService _chat = new("pet");
    private readonly WhisperService _whisper = new();
    private readonly VoiceRecorder _recorder = new();
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
    private bool _speaking; // 正在朗读回复（点击可打断）
    private int _listenRetries; // 连续「没听清」次数（防循环）
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

    /// <summary>朗读打断词（朗读期间专用监听，听到即停止回复）。</summary>
    private static readonly string[] InterruptWords =
    {
        "停止", "别说了", "闭嘴", "停一下", "好了好了", "别念了", "不要说了", "停下",
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

        Loaded += (_, _) =>
        {
            StartWakeListen();
            // 后台检测 whisper 可用性（python + faster_whisper + 模型），就绪后听写走离线识别
            _ = Task.Run(async () =>
            {
                try
                {
                    // 提示音预生成缓存（唤醒时零网络延迟）
                    await PetTtsService.EnsurePromptCachedAsync();
                    _whisper.CheckAvailability();
                    if (_whisper.IsAvailable)
                        Telemetry.Info("Pet.Whisper", "离线识别就绪");
                }
                catch (Exception ex)
                {
                    Telemetry.Error("Pet.Whisper", ex);
                }
            });
        };
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

    /// <summary>朗读期间监听打断词（专用 Grammar，不会被朗读声误触发成对话指令）。</summary>
    private void StartInterruptListen()
    {
        if (!_speechAvailable || !IsLoaded)
            return;
        _phase = SpeechPhase.Interrupt;
        var grammar = new Grammar(new Choices(InterruptWords));
        RunRecognition(grammar, _ =>
        {
            _phase = SpeechPhase.None;
            Dispatcher.Invoke(() =>
            {
                PetTtsService.Stop(); // 立即停止朗读
                Bubble.Visibility = Visibility.Visible;
                BubbleText.Text = "好的，不说了～";
            });
        });
    }

    /// <summary>停止打断监听（朗读结束/被打断后调用）。</summary>
    private void StopInterruptListen()
    {
        if (_phase != SpeechPhase.Interrupt)
            return;
        _phase = SpeechPhase.None;
        try { _currentEngine?.RecognizeAsyncCancel(); } catch { /* 忽略 */ }
    }

    /// <summary>开始一次听写：先语音提示「我在，请说」，播完再开始识别（防提示音被录进麦克风）。</summary>
    public void StartDictation()
    {
        if (!_speechAvailable || !IsLoaded)
            return;
        _phase = SpeechPhase.Dictate;
        Dispatcher.Invoke(() =>
        {
            Bubble.Visibility = Visibility.Visible;
            BubbleText.Text = _inConversation ? "🎤 请说…（再见 结束对话）" : "🎤 我在，请说…";
        });
        _ = PromptAndListenAsync();
    }

    /// <summary>播提示音（本地缓存即时播放）→ 开始录音/听写。</summary>
    private async Task PromptAndListenAsync()
    {
        try
        {
            await PetTtsService.PlayPromptAsync();
        }
        catch
        {
            // 提示音失败不影响听写
        }
        if (_phase != SpeechPhase.Dictate || !IsLoaded)
            return;

        if (_whisper.IsAvailable)
        {
            // whisper 路径：录音 → 静音自动停止 → 离线转写（准确率高）
            // 录音期间监听打断词（算了/停止/不说了…）→ 立即取消回待命
            StartCancelListen();
            _recorder.Completed += OnWhisperRecorded;
            _recorder.Start();
        }
        else
        {
            // SAPI 兜底：流式听写
            var grammar = new DictationGrammar();
            RunRecognition(grammar, text =>
            {
                _phase = SpeechPhase.None; // 已拿到结果：暂停监听
                Dispatcher.InvokeAsync(async () => await HandleDictationAsync(text));
            });
        }
    }

    /// <summary>录音期间监听打断词（算了/停止/不说了…）——命中即取消本轮听写。</summary>
    private void StartCancelListen()
    {
        try
        {
            var engine = new SpeechRecognitionEngine(new CultureInfo("zh-CN"));
            var choices = new Choices("算了", "停止", "不说了", "别说了", "取消", "取消听写", "不要了");
            engine.LoadGrammar(new Grammar(choices));
            engine.SpeechRecognized += (_, e) =>
            {
                var text = e.Result?.Text ?? "";
                if (!string.IsNullOrWhiteSpace(text) && _phase == SpeechPhase.Dictate)
                    Dispatcher.Invoke(CancelDictation);
            };
            engine.RecognizeAsync(RecognizeMode.Single);
            _currentEngine = engine;
        }
        catch
        {
            // 打断词监听失败不影响录音
        }
    }

    /// <summary>取消本轮听写：停止提示音/录音/监听，回待命。</summary>
    private void CancelDictation()
    {
        try
        {
            _recorder.Stop();
        }
        catch
        {
            // 忽略
        }
        PetTtsService.Stop();
        try
        {
            _currentEngine?.RecognizeAsyncCancel();
            _currentEngine?.Dispose();
        }
        catch
        {
            // 忽略
        }
        _currentEngine = null;
        _phase = SpeechPhase.None;
        _listenRetries = 0;
        _inConversation = false;
        BubbleText.Text = "好的～";
        StartWakeListen();
    }

    /// <summary>录音完成回调（whisper 路径）：置信度过滤 → 转写 → 交给 AI。</summary>
    private void OnWhisperRecorded(string? wavPath)
    {
        _recorder.Completed -= OnWhisperRecorded;
        // 停止打断词监听（录音已结束）
        try
        {
            _currentEngine?.RecognizeAsyncCancel();
            _currentEngine?.Dispose();
        }
        catch
        {
            // 忽略
        }
        _currentEngine = null;
        _phase = SpeechPhase.None;
        if (wavPath is null)
        {
            FailListen();
            return;
        }
        Dispatcher.InvokeAsync(async () =>
        {
            if (!IsLoaded)
                return;
            BubbleText.Text = "🧠 识别中…";
            var result = await _whisper.TranscribeAsync(wavPath);
            try { File.Delete(wavPath); } catch { /* 忽略 */ }

            var text = result?.Text?.Trim() ?? "";
            // 低置信（环境杂音/键盘声被误识别）或空 → 判定没听清，不发给 AI（防循环）
            if (text.Length == 0 || result!.Confidence < -0.6)
            {
                FailListen();
                return;
            }
            _listenRetries = 0;
            await HandleDictationAsync(text);
        });
    }

    /// <summary>没听清：重听，连续 2 次失败回唤醒监听（防无限循环）。</summary>
    private void FailListen()
    {
        _listenRetries++;
        BubbleText.Text = "没听清，再说一次？";
        Dispatcher.InvokeAsync(() =>
        {
            if (_listenRetries >= 2)
            {
                _listenRetries = 0;
                StartWakeListen();
            }
            else
            {
                StartDictation();
            }
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

        // 成功回复 → 语音朗读（朗读期间监听打断词，可被「停止/别说了」或点击打断）
        if (content is not null)
        {
            _speaking = true;
            StartInterruptListen();
            await PetTtsService.SpeakAsync(content);
            _speaking = false;
            StopInterruptListen();
        }

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
            Telemetry.Error("Pet.Speech", ex);            Dispatcher.Invoke(() =>
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
                case SpeechPhase.Interrupt:
                    StartInterruptListen(); // 朗读仍在继续，继续听打断词
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
            // 朗读中点击 = 打断回复并直接听新指令
            if (_speaking)
            {
                PetTtsService.Stop();
                StopInterruptListen();
                BubbleText.Text = "好的，请说…";
            }
            // 听写/录音/提示音阶段点击 = 取消本轮听写回待命（中途可打断）
            else if (_phase == SpeechPhase.Dictate)
            {
                CancelDictation();
                base.OnMouseLeftButtonUp(e);
                return;
            }
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
            _recorder.Dispose();
            _whisper.Dispose();
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
