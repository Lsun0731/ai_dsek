using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.AI;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 桌面宠物：VPet 动漫角色（待机/走路动画），可拖动，可自由走动，点击呼出输入框 → AI 回复头顶气泡。
/// 动画素材来源：VPet 开源桌宠（https://github.com/LorisYounger/VPet），非商用免费。
/// </summary>
public partial class PetWidgetWindow : WidgetWindowBase
{
    private enum PetState { Idle, Walking }

    private readonly AIChatClient _ai = new();
    private readonly List<ImageSource> _idleFrames = new();
    private readonly List<ImageSource> _walkFrames = new();
    private readonly Random _rnd = new();
    private int _frameIndex;
    private PetState _state = PetState.Idle;
    private int _walkDir = 1;          // 1 向右 / -1 向左
    private double _walkRemain;        // 剩余走动距离
    private DateTime _nextIdleEnd = DateTime.MinValue;
    private bool _chatMode;
    private bool _thinking;

    // 拖动检测（按下后轮询位移，超过阈值转系统 DragMove —— 丝滑）
    private DispatcherTimer? _dragDetect;
    private bool _dragStarted;

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

    /// <summary>走动一步：移动 + 边界掉头 + 到达目标转待机。</summary>
    private void TickWalk()
    {
        const double step = 3.0; // 每帧移动 3 DIP（~24 DIP/s）
        Left += _walkDir * step;
        _walkRemain -= step;

        var work = SystemParameters.WorkArea;
        if (Left <= work.Left || Left >= work.Right - ActualWidth)
            FlipDirection();
        if (_walkRemain <= 0)
        {
            _state = PetState.Idle;
            _nextIdleEnd = DateTime.Now.AddSeconds(_rnd.Next(8, 18));
        }
    }

    private void FlipDirection()
    {
        _walkDir = -_walkDir;
        PetImage.RenderTransformOrigin = new Point(0.5, 0.5);
        PetImage.RenderTransform = new ScaleTransform(_walkDir, 1);
    }

    /// <summary>待机状态随机决策：概率触发一次水平走动。</summary>
    private void DecideNextAction()
    {
        if (DateTime.Now < _nextIdleEnd)
            return;
        if (_rnd.NextDouble() < 0.65)
        {
            var work = SystemParameters.WorkArea;
            var minX = Math.Max(0, work.Left);
            var maxX = Math.Max(minX + 1, work.Right - ActualWidth);
            var targetX = _rnd.Next((int)minX, (int)maxX + 1);
            _walkDir = targetX >= Left ? 1 : -1;
            _walkRemain = Math.Abs(targetX - Left);
            _state = PetState.Walking;
            PetImage.RenderTransformOrigin = new Point(0.5, 0.5);
            PetImage.RenderTransform = new ScaleTransform(_walkDir, 1);
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

    // ---- AI 对话 ----

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

        var reply = await _ai.ChatAsync(settings, message);

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

    protected override void OnClosed(System.EventArgs e)
    {
        _dragDetect?.Stop();
        _ai.Dispose();
        base.OnClosed(e);
    }
}
