using System.Collections.ObjectModel;
using System.Diagnostics;
using AiDesk.Core.Cursors;
using AiDesk.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiDesk.App.ViewModels;

/// <summary>光标主题页 ViewModel。</summary>
public partial class CursorThemeViewModel : ObservableObject
{
    private readonly CursorThemeService _service = new();

    public ObservableCollection<string> Schemes { get; } = [];

    [ObservableProperty]
    private string? _selectedScheme;

    [ObservableProperty]
    private string _currentScheme = "未知";

    [ObservableProperty]
    private string _status = string.Empty;

    public CursorThemeViewModel()
    {
        Refresh();
    }

    /// <summary>加载系统已安装的光标方案。</summary>
    [RelayCommand]
    private void Refresh()
    {
        Schemes.Clear();
        foreach (var scheme in _service.GetSchemes())
            Schemes.Add(scheme);

        CurrentScheme = _service.GetCurrentScheme() ?? "Windows 默认";
        SelectedScheme = Schemes.Contains(CurrentScheme) ? CurrentScheme : null;
        Status = $"共 {Schemes.Count} 个方案";
    }

    [RelayCommand]
    private void Apply()
    {
        if (string.IsNullOrWhiteSpace(SelectedScheme))
        {
            Status = "请先选择一个光标方案";
            return;
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var ok = _service.ApplyScheme(SelectedScheme);
            Status = ok ? $"已应用「{SelectedScheme}」" : $"方案「{SelectedScheme}」不存在或已损坏";
            if (ok)
                CurrentScheme = SelectedScheme;
            Telemetry.Function("CursorTheme.Apply", ok, sw.ElapsedMilliseconds, $"scheme={SelectedScheme}");
        }
        catch (Exception ex)
        {
            Status = $"应用失败：{ex.Message}";
            Telemetry.Function("CursorTheme.Apply", false, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    [RelayCommand]
    private void RestoreDefault()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _service.RestoreDefault();
            CurrentScheme = "Windows 默认";
            Status = "已恢复 Windows 默认光标";
            Telemetry.Function("CursorTheme.Restore", true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Status = $"恢复失败：{ex.Message}";
            Telemetry.Function("CursorTheme.Restore", false, sw.ElapsedMilliseconds, ex.Message);
        }
    }
}
