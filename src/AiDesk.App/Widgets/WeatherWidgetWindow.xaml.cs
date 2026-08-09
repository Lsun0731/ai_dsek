using System.Windows;
using System.Windows.Input;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.Weather;

namespace AiDesk.App.Widgets;

/// <summary>
/// 天气小组件：城市 + 温度 + 图标 + 湿度/风/今日预报。
/// 数据源 wttr.in（免费无 key，走系统代理）；每 30 分钟自动刷新。
/// </summary>
public partial class WeatherWidgetWindow : WidgetWindowBase
{
    private readonly WeatherService _weather = new();
    private bool _loading;

    public WeatherWidgetWindow() : base(WidgetKind.Weather)
    {
        InitializeComponent();
        StartTicker(30 * 60); // 30 分钟
    }

    protected override void OnTick() => RefreshAsync();

    private async void RefreshAsync()
    {
        if (_loading)
            return;
        _loading = true;
        RefreshBtn.IsEnabled = false;

        var city = WidgetConfig.Load().WeatherCity;
        CityText.Text = city;

        try
        {
            var info = await _weather.GetWeatherAsync(city);
            if (info is null)
            {
                ShowError("天气数据解析失败");
                return;
            }
            IconText.Text = info.Icon;
            TempText.Text = $"{info.TempC:F0}°";
            DescText.Text = info.Description + (info.IsNight ? " · 夜间" : "");
            DetailText.Text =
                $"湿度 {info.Humidity}% · 风 {info.WindKmph:F0} km/h\n今日 {info.MinTempC:F0} ~ {info.MaxTempC:F0}°";
            Telemetry.Function("Widget.Weather", true, 0, $"city={city} temp={info.TempC}");
        }
        catch (Exception ex)
        {
            ShowError("天气获取失败（检查网络）");
            Telemetry.Function("Widget.Weather", false, 0, $"city={city} error={ex.Message}");
        }
        finally
        {
            _loading = false;
            RefreshBtn.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        DescText.Text = message;
        DetailText.Text = string.Empty;
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => RefreshAsync();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        _weather.Dispose();
        base.OnClosed(e);
    }
}
