using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDesk.App.ViewModels;

/// <summary>占位页面（功能开发中）。</summary>
public partial class PlaceholderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message;

    public PlaceholderViewModel(string message)
    {
        _message = message;
    }
}
