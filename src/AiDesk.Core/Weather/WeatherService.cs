using System.Net;
using System.Text.Json;

namespace AiDesk.Core.Weather;

/// <summary>天气信息。</summary>
public sealed record WeatherInfo(
    string City,
    double TempC,
    double FeelsLikeC,
    string Description,
    int Humidity,
    double WindKmph,
    double MinTempC,
    double MaxTempC,
    bool IsNight)
{
    /// <summary>天气描述对应的 emoji 图标。</summary>
    public string Icon => MapIcon(Description);

    public static string MapIcon(string description)
    {
        var d = description.ToLowerInvariant();
        if (d.Contains("雷") || d.Contains("thunder") || d.Contains("storm")) return "⛈️";
        if (d.Contains("雪") || d.Contains("冰") || d.Contains("snow") || d.Contains("blizzard") || d.Contains("sleet")) return "❄️";
        if (d.Contains("雨") || d.Contains("rain") || d.Contains("drizzle") || d.Contains("shower") || d.Contains("precipitation")) return d.Contains("阵") || d.Contains("shower") ? "🌦️" : "🌧️";
        if (d.Contains("雾") || d.Contains("霾") || d.Contains("烟") || d.Contains("fog") || d.Contains("mist") || d.Contains("haze")) return "🌫️";
        if (d.Contains("阴") || d.Contains("overcast")) return "☁️";
        if (d.Contains("晴") || d.Contains("clear") || d.Contains("sunny")) return "☀️";
        if (d.Contains("云") || d.Contains("cloud")) return "⛅";
        return "🌡️";
    }
}

/// <summary>
/// 天气服务：wttr.in（免费、无需 key）。网络走系统代理（用户环境 Clash 127.0.0.1:7897）。
/// </summary>
public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http;

    public WeatherService()
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.GetSystemWebProxy(),
            UseProxy = true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AiDesk/1.0");
    }

    /// <summary>按城市名获取实时天气 + 今日预报。</summary>
    public async Task<WeatherInfo?> GetWeatherAsync(string city, CancellationToken ct = default)
    {
        var url = $"https://wttr.in/{Uri.EscapeDataString(city)}?format=j1&lang=zh";
        var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>解析 wttr.in JSON（纯函数，可单测）。解析失败返回 null。</summary>
    public static WeatherInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("current_condition", out var cc) || cc.GetArrayLength() == 0)
                return null;

            var current = cc[0];
            var tempC = GetDouble(current, "temp_C");
            var feels = GetDouble(current, "FeelsLikeC");
            var humidity = GetInt(current, "humidity");
            var wind = GetDouble(current, "windspeedKmph");

            var desc = "未知";
            if (current.TryGetProperty("lang_zh", out var lang) && lang.GetArrayLength() > 0 &&
                lang[0].TryGetProperty("value", out var langValue))
                desc = langValue.GetString() ?? desc;
            else if (current.TryGetProperty("weatherDesc", out var wd) && wd.GetArrayLength() > 0 &&
                     wd[0].TryGetProperty("value", out var wdValue))
                desc = wdValue.GetString() ?? desc;

            // 今日预报（最低/最高温）
            var minTemp = tempC;
            var maxTemp = tempC;
            if (root.TryGetProperty("weather", out var weather) && weather.GetArrayLength() > 0)
            {
                minTemp = GetDouble(weather[0], "mintempC", tempC);
                maxTemp = GetDouble(weather[0], "maxtempC", tempC);
            }

            // 城市名（nearest_area）
            var cityName = "未知";
            if (root.TryGetProperty("nearest_area", out var area) && area.GetArrayLength() > 0 &&
                area[0].TryGetProperty("areaName", out var areaName) && areaName.GetArrayLength() > 0 &&
                areaName[0].TryGetProperty("value", out var nameValue))
                cityName = nameValue.GetString() ?? cityName;

            var isNight = false;
            if (current.TryGetProperty("isday", out var day))
                isNight = day.GetString() == "0";

            return new WeatherInfo(cityName, tempC, feels, desc, humidity, wind, minTemp, maxTemp, isNight);
        }
        catch
        {
            return null;
        }
    }

    private static double GetDouble(JsonElement e, string name, double fallback = 0) =>
        e.TryGetProperty(name, out var v) && double.TryParse(v.GetString(), out var d) ? d : fallback;

    private static int GetInt(JsonElement e, string name, int fallback = 0) =>
        e.TryGetProperty(name, out var v) && int.TryParse(v.GetString(), out var i) ? i : fallback;

    public void Dispose() => _http.Dispose();
}
