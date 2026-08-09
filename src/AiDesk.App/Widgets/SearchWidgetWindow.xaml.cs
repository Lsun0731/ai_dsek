using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>搜索小组件：输入即搜开始菜单应用，回车/双击启动。</summary>
public partial class SearchWidgetWindow : WidgetWindowBase
{
    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;

    public SearchWidgetWindow() : base(Services.WidgetKind.Search)
    {
        InitializeComponent();
        _startMenuApps = StartMenuAppsProvider.Scan();
    }

    protected override void OnWidgetLoaded() => SearchBox.Focus();

    protected override void OnTick()
    {
        // 搜索为交互驱动，无需轮询
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        List<StartMenuApp> results;
        if (string.IsNullOrEmpty(query))
            results = [];
        else
            results = _startMenuApps
                .Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(8)
                .ToList();

        SearchList.ItemsSource = results;
        SearchList.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = !string.IsNullOrEmpty(query) && results.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchList.Items.Count > 0 && SearchList.ItemsSource is not null)
            LaunchApp((StartMenuApp)SearchList.Items[0]);
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is StartMenuApp app)
            LaunchApp(app);
    }

    private void LaunchApp(StartMenuApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.LnkPath) { UseShellExecute = true });
            Telemetry.Function("Widget.Search.Launch", true, 0, $"app={app.Name}");
        }
        catch (Exception ex)
        {
            Telemetry.Function("Widget.Search.Launch", false, 0, $"app={app.Name} err={ex.Message}");
        }
    }
}
