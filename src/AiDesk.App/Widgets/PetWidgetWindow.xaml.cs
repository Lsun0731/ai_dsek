using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiDesk.App.Services;
using AiDesk.Core.AI;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 桌面宠物：VPet 动漫角色动画帧（待机循环），与 AI 对话联动（点击呼出输入框，回复显示头顶气泡）。
/// 动画素材来源：VPet 开源桌宠（https://github.com/LorisYounger/VPet），非商用免费。
/// </summary>
public partial class PetWidgetWindow : WidgetWindowBase
{
    private readonly AIChatClient _ai = new();
    private readonly List<ImageSource> _frames = new();
    private int _frameIndex;
    private bool _chatMode;
    private bool _thinking;

    public PetWidgetWindow() : base(Services.WidgetKind.Pet, topmost: true)
    {
        InitializeComponent();
        LoadFrames();
        StartTickerMs(125); // 帧时长 125ms
        ShowGreeting();
    }

    /// <summary>宠物禁用基类 DragMove（点击=聊天，拖动用手动位移实现，避免拖/点冲突）。</summary>
    protected override bool ShouldDrag(System.Windows.Input.MouseButtonEventArgs e) => false;

    // ---- 拖动 + 点击区分（按下/抬起位移判断） ----

    private Point _dragStartScreen;
    private Point _winStart;
    private bool _down;
    private bool _dragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _down = true;
        _dragging = false;
        _dragStartScreen = e.GetPosition(null);
        _winStart = new Point(Left, Top);
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_down && e.LeftButton == MouseButtonState.Pressed)
        {
            var cur = e.GetPosition(null);
            if (!_dragging &&
                (Math.Abs(cur.X - _dragStartScreen.X) > 5 || Math.Abs(cur.Y - _dragStartScreen.Y) > 5))
                _dragging = true;
            if (_dragging)
            {
                Left = _winStart.X + (cur.X - _dragStartScreen.X);
                Top = _winStart.Y + (cur.Y - _dragStartScreen.Y);
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_down)
        {
            _down = false;
            ReleaseMouseCapture();
            if (!_dragging)
                ToggleChat();
        }
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

    private void LoadFrames()
    {
        try
        {
            for (var i = 0; i < 17; i++)
            {
                var uri = new Uri($"pack://application:,,,/Assets/Pet/pet_{i:D3}.png");
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                _frames.Add(bitmap);
            }
            if (_frames.Count > 0)
                PetImage.Source = _frames[0];
        }
        catch (Exception ex)
        {
            Telemetry.Error("Pet.LoadFrames", ex);
        }
    }

    protected override void OnTick()
    {
        if (_frames.Count == 0)
            return;
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        PetImage.Source = _frames[_frameIndex];
    }

    private void ShowGreeting()
    {
        Bubble.Visibility = Visibility.Visible;
        BubbleText.Text = "你好呀！点我聊天～";
    }

    // ---- 点击互动 ----

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
        _ai.Dispose();
        base.OnClosed(e);
    }
}
