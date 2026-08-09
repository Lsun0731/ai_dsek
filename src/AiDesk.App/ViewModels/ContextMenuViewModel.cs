using System.Collections.ObjectModel;
using System.Diagnostics;
using AiDesk.Core.ContextMenu;
using AiDesk.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiDesk.App.ViewModels;

/// <summary>右键菜单项的可观察包装（用于列表展示与状态切换）。</summary>
public partial class ContextMenuItemViewModel : ObservableObject
{
    public ContextMenuItem Model { get; }

    /// <summary>展示名称（去掉禁用前缀 "-"）。</summary>
    public string Name => Model.RawKeyName.TrimStart('-');

    public string LocationDisplay => Model.LocationDisplay;

    public string? Command => Model.Command;

    public string RegistryPath => Model.RegistryPath;

    public bool IsExtension => Model.IsExtension;

    [ObservableProperty]
    private bool _isEnabled;

    public ContextMenuItemViewModel(ContextMenuItem model)
    {
        Model = model;
        _isEnabled = !model.IsDisabled;
    }
}

/// <summary>位置筛选选项。</summary>
public sealed class LocationFilter
{
    public ContextMenuLocation? Value { get; }
    public string Display { get; }

    private LocationFilter(ContextMenuLocation? value, string display)
    {
        Value = value;
        Display = display;
    }

    public static LocationFilter All { get; } = new(null, "全部位置");

    public static IReadOnlyList<LocationFilter> CreateAll()
    {
        var list = new List<LocationFilter> { All };
        list.AddRange(Enum.GetValues<ContextMenuLocation>()
            .Select(l => new LocationFilter(l, ContextMenuItem.GetLocationDisplay(l))));
        return list;
    }
}

/// <summary>右键菜单管理页 ViewModel。</summary>
public partial class ContextMenuViewModel : ObservableObject
{
    private readonly ContextMenuService _service = new();

    public ObservableCollection<ContextMenuItemViewModel> Items { get; } = [];

    public IReadOnlyList<LocationFilter> Locations { get; } = LocationFilter.CreateAll();

    [ObservableProperty]
    private LocationFilter? _selectedLocation;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ContextMenuViewModel()
    {
        SelectedLocation = LocationFilter.All;
    }

    partial void OnSelectedLocationChanged(LocationFilter? value) => Refresh();

    /// <summary>加载全部菜单项（可按位置筛选）。</summary>
    [RelayCommand]
    private void Refresh()
    {
        var sw = Stopwatch.StartNew();
        IsLoading = true;
        try
        {
            var all = _service.Enumerate();
            var filtered = SelectedLocation?.Value is { } loc
                ? all.Where(i => i.Location == loc)
                : all;

            Items.Clear();
            foreach (var item in filtered.OrderBy(i => i.Location).ThenBy(i => i.RawKeyName))
                Items.Add(new ContextMenuItemViewModel(item));

            StatusText = $"共 {filtered.Count()} 项";
            Telemetry.Function("ContextMenu.Refresh", true, sw.ElapsedMilliseconds, $"count={filtered.Count()} location={SelectedLocation?.Display}");
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败：{ex.Message}";
            Telemetry.Function("ContextMenu.Refresh", false, sw.ElapsedMilliseconds, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>启用/禁用一项（注册表重命名，可逆）。</summary>
    public bool ToggleItem(ContextMenuItemViewModel item, bool enabled)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _service.SetEnabled(item.Model, enabled);
            item.IsEnabled = enabled;
            StatusText = enabled
                ? $"已启用「{item.Name}」"
                : $"已禁用「{item.Name}」（可在原位置重新启用）";
            Telemetry.Function("ContextMenu.ToggleItem", true, sw.ElapsedMilliseconds,
                $"name={item.Name} enabled={enabled} location={item.LocationDisplay}");
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"操作失败：{ex.Message}";
            Telemetry.Function("ContextMenu.ToggleItem", false, sw.ElapsedMilliseconds,
                $"name={item.Name} enabled={enabled} error={ex.Message}");
            return false;
        }
    }

    /// <summary>删除一项（不可逆，调用方必须先确认）。</summary>
    public bool DeleteItem(ContextMenuItemViewModel item)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _service.Delete(item.Model);
            Items.Remove(item);
            StatusText = $"已删除「{item.Name}」";
            Telemetry.Function("ContextMenu.DeleteItem", true, sw.ElapsedMilliseconds,
                $"name={item.Name} path={item.RegistryPath}");
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
            Telemetry.Function("ContextMenu.DeleteItem", false, sw.ElapsedMilliseconds,
                $"name={item.Name} error={ex.Message}");
            return false;
        }
    }

    /// <summary>匹配推荐精简清单（只读），返回界面展示用的命中项。</summary>
    public IReadOnlyList<(string Name, string Description, string Location)> PrepareRecommended()
    {
        return _service.FindRecommended(RecommendedDisableList.All)
            .Select(m => (m.Item.RawKeyName, m.Definition.Description, m.Item.LocationDisplay))
            .ToList();
    }

    /// <summary>批量禁用推荐清单中的菜单项并刷新列表；成功返回禁用数量，失败返回 -1（状态栏有原因）。</summary>
    public int ApplyRecommended()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var count = _service.DisableRecommended(RecommendedDisableList.All);
            Refresh();
            StatusText = $"已禁用 {count} 个系统冗余菜单项（可随时重新启用）";
            Telemetry.Function("ContextMenu.ApplyRecommended", true, sw.ElapsedMilliseconds, $"count={count}");
            return count;
        }
        catch (Exception ex)
        {
            StatusText = $"推荐精简失败：{ex.Message}";
            Telemetry.Function("ContextMenu.ApplyRecommended", false, sw.ElapsedMilliseconds, ex.Message);
            return -1;
        }
    }
}
