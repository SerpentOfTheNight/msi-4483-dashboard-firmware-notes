using System.IO;
using System.Text.Json;

namespace PanelRuntime.Models;

public sealed class RuntimeConfig
{
    /// <summary>"auto" | "en" | "zh"</summary>
    public string UiLanguage { get; set; } = "auto";
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 800;
    public double Fps { get; set; } = 5.0;
    public double IdleFps { get; set; } = 1.0;
    public double ActiveFps { get; set; } = 5.0;
    public int ActiveHoldMs { get; set; } = 3000;
    public int SensorPollMs { get; set; } = 1000;
    public int StorageHealthPollMs { get; set; } = 60000;
    public int DashboardPollEveryFrames { get; set; } = 5;
    public int DashboardPollMs { get; set; } = 1000;
    public int PresentKeepAliveMs { get; set; } = 250;
    public int NucHeartbeatMs { get; set; } = 1000;
    public bool TouchEnabled { get; set; } = true;
    public bool TouchAutoSwapXY { get; set; } = true;
    public bool ClearBuffersOnExit { get; set; } = true;
    public bool RestoreDisplayOnExit { get; set; } = true;
    public string RestoreDisplayMode { get; set; } = "image";
    public int RestoreImageIndex { get; set; } = 7;
    public string PanelHtml { get; set; } = "assets/panel/index.html";
    public string[] Buffers { get; set; } = ["0x008C35E0", "0x0097EDE0", "0x00A3A5E0"];
    public bool PreviewVisible { get; set; } = true;
    public bool SendToDevice { get; set; }
    public string Mode { get; set; } = "preview-only";

    public string MediaFolder { get; set; } = "";
    public int MediaIntervalMs { get; set; } = 8000;
    public string MediaVirtualHost { get; set; } = "panel-media.local";

    public List<RssFeedConfig> RssFeeds { get; set; } = new();

    public int RssDisplayRefreshMs { get; set; } = 18000;
    public int RssPollMinutes { get; set; } = 15;
    public double WeatherLatitude { get; set; } = 44.4142;
    public double WeatherLongitude { get; set; } = -110.5786;
    public string WeatherTempUnit { get; set; } = "fahrenheit"; // or celsius
    public int WeatherPollMinutes { get; set; } = 20;

    
    public Dictionary<string, string> NetworkAdapters { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wifi"] = "WLAN",
        ["ethernet10g"] = "以太网",
        ["ethernet25g"] = "以太网 2"
    };

    public static RuntimeConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new RuntimeConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RuntimeConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RuntimeConfig();
    }

    public uint[] ParseBuffers()
    {
        if (Buffers.Length < 2)
        {
            throw new InvalidOperationException("At least two frame buffers are required.");
        }

        return Buffers.Select(ParseU32).Distinct().ToArray();
    }

    public static uint ParseU32(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt32(text[2..], 16);
        }
        return Convert.ToUInt32(text, 10);
    }
    
}
public sealed class RssFeedConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}