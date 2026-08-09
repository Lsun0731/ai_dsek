using AiDesk.Core.Weather;

namespace AiDesk.Core.Tests.Weather;

public class WeatherServiceTests
{
    private const string SampleJson = """
    {
      "current_condition": [
        {
          "FeelsLikeC": "31",
          "humidity": "39",
          "temp_C": "32",
          "windspeedKmph": "12",
          "isday": "1",
          "lang_zh": [ { "value": "晴" } ],
          "weatherDesc": [ { "value": "Sunny" } ]
        }
      ],
      "weather": [
        { "mintempC": "24", "maxtempC": "35" }
      ],
      "nearest_area": [
        { "areaName": [ { "value": "北京" } ] }
      ]
    }
    """;

    [Fact]
    public void Parse_有效JSON_返回完整天气信息()
    {
        var info = WeatherService.Parse(SampleJson);

        Assert.NotNull(info);
        Assert.Equal("北京", info!.City);
        Assert.Equal(32, info.TempC);
        Assert.Equal(31, info.FeelsLikeC);
        Assert.Equal("晴", info.Description);
        Assert.Equal(39, info.Humidity);
        Assert.Equal(12, info.WindKmph);
        Assert.Equal(24, info.MinTempC);
        Assert.Equal(35, info.MaxTempC);
        Assert.False(info.IsNight);
    }

    [Fact]
    public void Parse_夜间_IsNight为True()
    {
        var json = SampleJson.Replace("\"isday\": \"1\"", "\"isday\": \"0\"");
        var info = WeatherService.Parse(json);

        Assert.NotNull(info);
        Assert.True(info!.IsNight);
    }

    [Fact]
    public void Parse_损坏JSON_返回null()
    {
        Assert.Null(WeatherService.Parse("not json at all"));
        Assert.Null(WeatherService.Parse(""));
        Assert.Null(WeatherService.Parse("{\"a\": 1}"));
    }

    [Fact]
    public void Parse_缺字段_使用默认值()
    {
        var json = """
        {
          "current_condition": [ { "temp_C": "28" } ]
        }
        """;
        var info = WeatherService.Parse(json);

        Assert.NotNull(info);
        Assert.Equal(28, info!.TempC);
        Assert.Equal(0, info.Humidity);      // 缺省
        Assert.Equal("未知", info.Description); // 缺省
        Assert.Equal(28, info.MinTempC);     // 缺省=当前温
        Assert.Equal(28, info.MaxTempC);
    }

    [Theory]
    [InlineData("雷阵雨", "⛈️")]
    [InlineData("大雪", "❄️")]
    [InlineData("小雨", "🌧️")]
    [InlineData("雾", "🌫️")]
    [InlineData("阴", "☁️")]
    [InlineData("晴", "☀️")]
    [InlineData("多云", "☁️")]
    [InlineData("未知描述", "🌡️")]
    public void MapIcon_描述映射emoji(string description, string expected)
    {
        Assert.Equal(expected, WeatherInfo.MapIcon(description));
    }
}
