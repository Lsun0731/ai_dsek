using System.Diagnostics;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.Taskbar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiDesk.App.ViewModels;

/// <summary>任务栏美化页 ViewModel。</summary>
public partial class TaskbarViewModel : ObservableObject
{
    /// <summary>效果选项（显示名, 枚举值）。</summary>
    public sealed record EffectOption(string Display, TaskbarEffect Value);

    public IReadOnlyList<EffectOption> Effects { get; } =
    [
        new("恢复默认", TaskbarEffect.Default),
        new("全透明", TaskbarEffect.Transparent),
        new("毛玻璃模糊", TaskbarEffect.Blur),
        new("亚克力", TaskbarEffect.Acrylic),
    ];

    [ObservableProperty]
    private EffectOption? _selectedEffect;

    /// <summary>着色强度 0-100（透明/亚克力有效）。</summary>
    [ObservableProperty]
    private int _tintStrength = 50;

    [ObservableProperty]
    private string _status = string.Empty;

    public TaskbarViewModel()
    {
        SelectedEffect = Effects.First(e => e.Value == TaskbarEffect.Acrylic);
    }

    [RelayCommand]
    private void Apply()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var alpha = (uint)(TintStrength * 255 / 100);
            var color = (alpha << 24) | 0x000000; // ABGR：黑色 + 指定不透明度
            var ok = TaskbarService.ApplyEffect(SelectedEffect?.Value ?? TaskbarEffect.Default, color);
            Status = ok
                ? $"已应用「{SelectedEffect?.Display}」"
                : "应用失败：当前系统（Windows 11 任务栏）可能不支持该特效";
            Telemetry.Function("Taskbar.Apply", ok, sw.ElapsedMilliseconds,
                $"effect={SelectedEffect?.Display} tint={TintStrength}");
        }
        catch (Exception ex)
        {
            Status = $"应用失败：{ex.Message}";
            Telemetry.Function("Taskbar.Apply", false, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        var ok = TaskbarService.ApplyEffect(TaskbarEffect.Default);
        Status = ok ? "已恢复系统默认任务栏" : "恢复失败：当前系统可能不支持该特效";
        Telemetry.Event("Taskbar", "恢复默认", ok ? "成功" : "失败");
    }
}
