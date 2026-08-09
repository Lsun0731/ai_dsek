namespace AiDesk.App;

/// <summary>导航项模型。</summary>
public sealed class NavItem
{
    public string Title { get; }
    public string? Icon { get; }
    public object? ViewModel { get; }
    public bool IsGroup { get; }
    public bool IsBottom { get; }

    public NavItem(string title, string? icon, object? viewModel = null, bool isGroup = false, bool isBottom = false)
    {
        Title = title;
        Icon = icon;
        ViewModel = viewModel;
        IsGroup = isGroup;
        IsBottom = isBottom;
    }
}
