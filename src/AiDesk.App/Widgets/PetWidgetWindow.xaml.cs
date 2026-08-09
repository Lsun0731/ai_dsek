using System.Windows;
using System.Windows.Input;
using AiDesk.App.Services;
using AiDesk.Core.AI;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 桌面宠物：程序化小猫角色（眨眼/呼吸动画、可拖动、点击互动）。
/// 与 AI 对话联动：点宠物呼出输入框，AI 回复显示在头顶气泡。
/// </summary>
public partial class PetWidgetWindow : WidgetWindowBase
{
    private readonly AIChatClient _ai = new();
    private int _tick;
    private bool _chatMode;
    private bool _thinking;

    public PetWidgetWindow() : base(Services.WidgetKind.Pet, topmost: true)
    {
        InitializeComponent();
        StartTickerMs(150); // 动画帧（毫秒）
        ShowGreeting();
    }

    /// <summary>宠物禁用拖动（点击=聊天，避免拖/点冲突）。</summary>
    protected override bool ShouldDrag(System.Windows.Input.MouseButtonEventArgs e) => false;

    protected override void OnTick()
    {
        _tick++;

        // 眨眼：每 20 帧（约 3 秒）闭眼 2 帧（约 0.3 秒）
        var phase = _tick % 24;
        if (phase == 0 || phase == 1)
            SetEyesClosed(true);
        else
            SetEyesClosed(false);

        // 呼吸浮动
        if (PetBody is not null)
        {
            var breath = Math.Sin(_tick * 0.12) * 2.5;
            PetBody.Margin = new Thickness(0, breath, 0, 0);
        }
    }

    private void SetEyesClosed(bool closed)
    {
        if (EyeL.Height == (closed ? 3 : 18))
            return;
        EyeL.Height = EyeR.Height = closed ? 3 : 18;
        EyeL.Margin = closed
            ? new Thickness(-22, 4, 0, 0)
            : new Thickness(-22, 0, 0, 0);
        EyeR.Margin = closed
            ? new Thickness(0, 4, -22, 0)
            : new Thickness(0, 0, -22, 0);
    }

    private void ShowGreeting()
    {
        Bubble.Visibility = Visibility.Visible;
        BubbleText.Text = "你好呀！点我聊天～";
    }

    // ---- 点击互动 ----

    private void OnPetBodyClick(object sender, MouseButtonEventArgs e)
    {
        if (_thinking)
            return;
        _chatMode = !_chatMode;
        InputPanel.Visibility = _chatMode ? Visibility.Visible : Visibility.Collapsed;
        BubbleText.Visibility = _chatMode ? Visibility.Collapsed : Visibility.Visible;
        Bubble.Visibility = Visibility.Visible;
        if (_chatMode)
        {
            InputBox.Focus();
        }
        else
        {
            ShowGreeting();
        }
    }

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
