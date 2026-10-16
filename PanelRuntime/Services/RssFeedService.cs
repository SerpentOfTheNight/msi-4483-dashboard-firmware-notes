using System.Net.Http;
using System.Xml.Linq;
using PanelRuntime.Models;

namespace PanelRuntime.Services;

public sealed class RssItemDto
{
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";  // blurb
    public string Link { get; set; } = "";
    public string Published { get; set; } = "";
}

public sealed class RssSnapshot
{
    public bool Online { get; set; }
    public string Error { get; set; } = "";
    public List<RssItemDto> Items { get; set; } = new();
    public long UpdatedAt { get; set; }
}

public sealed class RssFeedService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly RuntimeConfig _config;

    public RssFeedService(RuntimeConfig config)
    {
        _config = config;
    }

    public async Task<RssSnapshot> ReadAsync(CancellationToken token = default)
    {
        List<RssFeedConfig> feeds;
        if (_config.RssFeeds is null || _config.RssFeeds.Count == 0)
        {
            feeds = new List<RssFeedConfig>();
        }
        else
        {
            feeds = _config.RssFeeds
                .Where(f => !string.IsNullOrWhiteSpace(f.Url))
                .ToList();
        }

        if (feeds.Count == 0)
        {
            return new RssSnapshot
            {
                Online = false,
                Error = "No RSS feeds configured.",
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        var bag = new List<(DateTimeOffset When, RssItemDto Item)>();
        var errors = new List<string>();

        foreach (var feed in feeds)
        {
            try
            {
                using var response = await Http.GetAsync(feed.Url.Trim(), token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var xml = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                ParseFeed(xml, string.IsNullOrWhiteSpace(feed.Name) ? feed.Url : feed.Name, bag);
            }
            catch (Exception ex)
            {
                errors.Add($"{feed.Name}: {ex.Message}");
            }
        }

        var items = bag
            .OrderByDescending(x => x.When)
            .Select(x => x.Item)
            .Take(40)
            .ToList();

        return new RssSnapshot
        {
            Online = items.Count > 0,
            Error = items.Count == 0 && errors.Count > 0 ? string.Join("; ", errors) : "",
            Items = items,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static void ParseFeed(
        string xml,
        string sourceName,
        List<(DateTimeOffset When, RssItemDto Item)> bag)
    {
        var doc = XDocument.Parse(xml, LoadOptions.None);
        var root = doc.Root ?? throw new InvalidOperationException("Invalid feed XML.");

        // RSS 2.0
        var channelItems = root.Descendants("item").ToList();
        if (channelItems.Count > 0)
        {
            foreach (var item in channelItems)
            {
                var title = (string?)item.Element("title") ?? "";
                var link = (string?)item.Element("link") ?? "";
                var pub = (string?)item.Element("pubDate") ?? "";
                var when = ParseDate(pub) ?? DateTimeOffset.MinValue;
                var desc = (string?)item.Element("description")
                    ?? (string?)item.Element("{http://purl.org/rss/1.0/modules/content/}encoded")
                    ?? "";

                bag.Add((when, new RssItemDto
                {
                    Source = sourceName,
                    Title = Clean(title),
                    Summary = Truncate(Clean(desc), 200),
                    Link = link.Trim(),
                    Published = when == DateTimeOffset.MinValue ? pub : when.ToLocalTime().ToString("g")
                }));
            }
            return;
        }

        // Atom
        XNamespace atom = "http://www.w3.org/2005/Atom";
        foreach (var entry in root.Descendants(atom + "entry"))
        {
            var title = (string?)entry.Element(atom + "title") ?? "";
            var link = (string?)entry.Elements(atom + "link")
                .FirstOrDefault(e => (string?)e.Attribute("rel") is null or "alternate")
                ?.Attribute("href")
                ?? (string?)entry.Element(atom + "link")?.Attribute("href")
                ?? "";
            var pub = (string?)entry.Element(atom + "updated")
                ?? (string?)entry.Element(atom + "published")
                ?? "";
            var when = ParseDate(pub) ?? DateTimeOffset.MinValue;
            var summary = (string?)entry.Element(atom + "summary")
                ?? (string?)entry.Element(atom + "content")
                ?? "";
            // set Summary = Truncate(Clean(summary), 160)
            bag.Add((when, new RssItemDto
            {
                Source = sourceName,
                Title = Clean(title),
                Link = link.Trim(),
                Published = when == DateTimeOffset.MinValue ? pub : when.ToLocalTime().ToString("g")
            }));
        }
    }

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTimeOffset.TryParse(text, out var dto)) return dto;
        return null;
    }

    private static string Clean(string title)
    {
        title = System.Net.WebUtility.HtmlDecode(title ?? "").Trim();
        // strip simple tags if a feed includes HTML in titles
        if (title.Contains('<'))
        {
            try
            {
                title = System.Text.RegularExpressions.Regex.Replace(title, "<[^>]+>", " ");
                title = System.Net.WebUtility.HtmlDecode(title).Trim();
            }
            catch { /* keep raw */ }
        }
        return title;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PanelRuntime/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml, */*");
        return http;
    }

    private static string Truncate(string text, int max)
    {
        text = (text ?? "").Trim();
        if (text.Length <= max) return text;
            return text[..(max - 1)].TrimEnd() + "…";
    }
}