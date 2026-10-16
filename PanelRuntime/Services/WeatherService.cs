using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PanelRuntime.Models;

namespace PanelRuntime.Services;

public sealed class WeatherDayDto
{
    public string Label { get; set; } = "";
    public double High { get; set; }
    public double Low { get; set; }
    public string Condition { get; set; } = "";
    public int WeatherCode { get; set; }
}

public sealed class WeatherSnapshot
{
    public bool Online { get; set; }
    public string Error { get; set; } = "";
    public double Temp { get; set; }
    public double Humidity { get; set; }
    public double Wind { get; set; }
    public string Condition { get; set; } = "";
    public int WeatherCode { get; set; }
    public string Unit { get; set; } = "°F";
    public List<WeatherDayDto> Days { get; set; } = new();
    public long UpdatedAt { get; set; }
}

public sealed class WeatherService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RuntimeConfig _config;

    public WeatherService(RuntimeConfig config)
    {
        _config = config;
    }

    public async Task<WeatherSnapshot> ReadAsync(CancellationToken token = default)
    {
        try
        {
            var unit = string.Equals(_config.WeatherTempUnit, "celsius", StringComparison.OrdinalIgnoreCase)
                ? "celsius"
                : "fahrenheit";
            var unitSymbol = unit == "celsius" ? "°C" : "°F";
            var windUnit = unit == "celsius" ? "kmh" : "mph";

            var url =
                "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={_config.WeatherLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $"&longitude={_config.WeatherLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                $"&temperature_unit={unit}" +
                $"&wind_speed_unit={windUnit}" +
                "&timezone=auto" +
                "&forecast_days=5";

            using var response = await Http.GetAsync(url, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var data = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOpts, token)
                .ConfigureAwait(false);

            if (data?.Current is null || data.Daily is null)
            {
                return Offline("Empty weather payload.");
            }

            var days = new List<WeatherDayDto>();
            var n = Math.Min(
                data.Daily.Time?.Count ?? 0,
                Math.Min(
                    data.Daily.Temperature2mMax?.Count ?? 0,
                    Math.Min(
                        data.Daily.Temperature2mMin?.Count ?? 0,
                        data.Daily.WeatherCode?.Count ?? 0)));

            for (var i = 0; i < n; i++)
            {
                var code = data.Daily.WeatherCode![i];
                days.Add(new WeatherDayDto
                {
                    Label = FormatDayLabel(data.Daily.Time![i]),
                    High = data.Daily.Temperature2mMax![i],
                    Low = data.Daily.Temperature2mMin![i],
                    WeatherCode = code,
                    Condition = DescribeWeather(code)
                });
            }

            var currentCode = data.Current.WeatherCode;
            return new WeatherSnapshot
            {
                Online = true,
                Temp = data.Current.Temperature2m,
                Humidity = data.Current.RelativeHumidity2m,
                Wind = data.Current.WindSpeed10m,
                WeatherCode = currentCode,
                Condition = DescribeWeather(currentCode),
                Unit = unitSymbol,
                Days = days,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        catch (Exception ex)
        {
            return Offline(ex.Message);
        }
    }

    private static WeatherSnapshot Offline(string error) => new()
    {
        Online = false,
        Error = error,
        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    public static string DescribeWeather(int code) => code switch
    {
        0 => "Clear",
        1 or 2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Clouds"
    };

    private static string FormatDayLabel(string isoDate)
    {
        if (DateTime.TryParse(isoDate, out var dt))
        {
            return dt.ToString("ddd");
        }
        return isoDate;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PanelRuntime/1.0");
        return http;
    }

    private sealed class OpenMeteoResponse
    {
        public CurrentBlock? Current { get; set; }
        public DailyBlock? Daily { get; set; }
    }

    private sealed class CurrentBlock
    {
        [JsonPropertyName("temperature_2m")]
        public double Temperature2m { get; set; }

        [JsonPropertyName("relative_humidity_2m")]
        public double RelativeHumidity2m { get; set; }

        [JsonPropertyName("weather_code")]
        public int WeatherCode { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public double WindSpeed10m { get; set; }
    }

    private sealed class DailyBlock
    {
        public List<string>? Time { get; set; }

        [JsonPropertyName("weather_code")]
        public List<int>? WeatherCode { get; set; }

        [JsonPropertyName("temperature_2m_max")]
        public List<double>? Temperature2mMax { get; set; }

        [JsonPropertyName("temperature_2m_min")]
        public List<double>? Temperature2mMin { get; set; }
    }
}