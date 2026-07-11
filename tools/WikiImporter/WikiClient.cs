using System.Text.Json;

namespace Tsundosika.LimbusAssistant.WikiImporter;

public sealed class WikiClient : IDisposable
{
    const string ApiUrl = "https://limbuscompany.wiki.gg/api.php";
    const int BatchSize = 50;

    readonly HttpClient _http;

    public WikiClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LimbusAssistant-DatasetImporter/1.0");
    }

    public async Task<List<string>> ListCategoryMembersAsync(string category)
    {
        var titles = new List<string>();
        string? cmcontinue = null;
        do
        {
            var url = $"{ApiUrl}?action=query&list=categorymembers&cmtitle={Uri.EscapeDataString(category)}&cmlimit=500&format=json";
            if (cmcontinue is not null)
            {
                url += $"&cmcontinue={Uri.EscapeDataString(cmcontinue)}";
            }
            using var document = JsonDocument.Parse(await _http.GetStringAsync(url));
            foreach (var member in document.RootElement.GetProperty("query").GetProperty("categorymembers").EnumerateArray())
            {
                titles.Add(member.GetProperty("title").GetString()!);
            }
            cmcontinue = document.RootElement.TryGetProperty("continue", out var cont)
                ? cont.GetProperty("cmcontinue").GetString()
                : null;
        }
        while (cmcontinue is not null);
        return titles;
    }

    public async Task<Dictionary<string, string>> FetchPagesAsync(IReadOnlyList<string> titles, Action<int, int>? progress = null)
    {
        var pages = new Dictionary<string, string>();
        for (var offset = 0; offset < titles.Count; offset += BatchSize)
        {
            var batch = titles.Skip(offset).Take(BatchSize);
            var url = $"{ApiUrl}?action=query&prop=revisions&rvprop=content&rvslots=main&format=json"
                + $"&titles={Uri.EscapeDataString(string.Join("|", batch))}";
            using var document = JsonDocument.Parse(await _http.GetStringAsync(url));
            foreach (var page in document.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
            {
                if (!page.Value.TryGetProperty("revisions", out var revisions))
                {
                    continue;
                }
                var title = page.Value.GetProperty("title").GetString()!;
                var content = revisions[0].GetProperty("slots").GetProperty("main").GetProperty("*").GetString();
                if (content is not null)
                {
                    pages[title] = content;
                }
            }
            progress?.Invoke(Math.Min(offset + BatchSize, titles.Count), titles.Count);
        }
        return pages;
    }

    public void Dispose() => _http.Dispose();
}
