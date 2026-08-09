using System.Runtime.InteropServices;

namespace AiDesk.Core.Taskbar;

/// <summary>任务栏特效状态。</summary>
public enum TaskbarEffect
{
    /// <summary>恢复系统默认</summary>
    Default,

    /// <summary>全透明（无模糊）</summary>
    Transparent,

    /// <summary>模糊（毛玻璃）</summary>
    Blur,

    /// <summary>亚克力（Win10 1803+）</summary>
    Acrylic,
}

/// <summary>
/// 任务栏透明/毛玻璃特效服务。
/// 通过 SetWindowCompositionAttribute（Undocumented API，TranslucentTB 同款）应用到任务栏窗口 Shell_TrayWnd。
/// 注意：Windows 11 的任务栏为 XAML 实现，部分版本对该 API 不响应（需实测，失败返回 false）。
/// </summary>
public static class TaskbarService
{
    private const string TrayWindowClass = "Shell_TrayWnd";

    private enum AccentState
    {
        Disabled = 0,
        EnableGradient = 1,
        EnableTransparentGradient = 2,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
        EnableHostBackdrop = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // ABGR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;

    /// <summary>应用任务栏特效。返回是否成功（Win11 部分版本可能不响应）。</summary>
    /// <param name="effect">特效类型。</param>
    /// <param name="gradientColor">透明/亚克力着色（ABGR 格式，如 0x80FFFFFF = 半透明白）；Default 时忽略。</param>
    public static bool ApplyEffect(TaskbarEffect effect, uint gradientColor = 0x80000000)
    {
        var trayHwnd = FindWindow(TrayWindowClass, null);
        if (trayHwnd == nint.Zero)
            return false;

        var accentState = effect switch
        {
            TaskbarEffect.Default => AccentState.Disabled,
            TaskbarEffect.Transparent => AccentState.EnableTransparentGradient,
            TaskbarEffect.Blur => AccentState.EnableBlurBehind,
            TaskbarEffect.Acrylic => AccentState.EnableAcrylicBlurBehind,
            _ => AccentState.Disabled,
        };

        var accent = new AccentPolicy
        {
            AccentState = (int)accentState,
            AccentFlags = effect == TaskbarEffect.Transparent ? 2 : 0,
            GradientColor = unchecked((int)gradientColor),
        };
        var data = new WindowCompositionAttributeData
        {
            Attribute = WcaAccentPolicy,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
            SizeOfData = Marshal.SizeOf<AccentPolicy>(),
        };
        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            return SetWindowCompositionAttribute(trayHwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }
}
