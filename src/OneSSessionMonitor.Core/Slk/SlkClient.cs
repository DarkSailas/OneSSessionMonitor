using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OneSSessionMonitor.Core.Slk;

public interface ISlkClient
{
    ValueTask<SlkServerStatus> GetSlkStatusAsync(string? endpoint = null, string? user = null, string? password = null, CancellationToken cancellationToken = default);
}

public sealed class SlkClient : ISlkClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SlkClient>? _logger;

    public SlkClient(HttpClient? httpClient = null, ILogger<SlkClient>? logger = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3.0) };
        _logger = logger;
    }

    public async ValueTask<SlkServerStatus> GetSlkStatusAsync(string? endpoint = null, string? user = null, string? password = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new SlkServerStatus(false, "Не настроен", 0, 0, 0, 0, [], [], "Сервер СЛК не указан в настройках");
        }

        string cleanEp = endpoint.Trim();
        if (cleanEp.Contains(' ') && !cleanEp.Contains('-'))
        {
            cleanEp = cleanEp.Replace(" ", "-").Replace("-:", ":");
        }

        string baseUri = cleanEp.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || cleanEp.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? cleanEp
            : $"http://{cleanEp}";

        if (!baseUri.EndsWith('/')) baseUri += "/";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3.5));

        try
        {
            // 1. Опрос корневой веб-консоли СЛК 3.0 / 2.0
            var req = CreateRequest(new Uri(baseUri), user, password);
            var res = await _http.SendAsync(req, cts.Token);
            if (res.IsSuccessStatusCode)
            {
                byte[] bytes = await res.Content.ReadAsByteArrayAsync(cts.Token);
                string html = Encoding.UTF8.GetString(bytes);
                var parsed = ParseSlkHtml(html);
                if (parsed != null && (parsed.TotalLicenses > 0 || parsed.TotalKeys > 0 || parsed.InUseLicenses > 0))
                {
                    return parsed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Опрос веб-консоли СЛК не удался");
        }

        try
        {
            // 2. Опрос REST API СЛК
            var reqApi = CreateRequest(new Uri(baseUri + "api/v1/licenses"), user, password);
            var resApi = await _http.SendAsync(reqApi, cts.Token);
            if (resApi.IsSuccessStatusCode)
            {
                string json = await resApi.Content.ReadAsStringAsync(cts.Token);
                var parsedJson = ParseSlkJson(json);
                if (parsedJson != null) return parsedJson;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "REST API опрос СЛК не удался");
        }

        return new SlkServerStatus(false, cleanEp, 0, 0, 0, 0, [], [], "Сервер СЛК недоступен");
    }

    private static HttpRequestMessage CreateRequest(Uri uri, string? user, string? password)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(user))
        {
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password ?? string.Empty}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        }
        return req;
    }

    public static SlkServerStatus? ParseSlkHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        int totalKeys = 0;
        int totalLicenses = 0;
        int inUseLicenses = 0;
        var products = new List<SlkProductInfo>();
        var activeSessions = new List<SlkSessionHolder>();

        // 1. Поиск серийных блоков продуктов СЛК 3.0: <li id="60A2" class="serie li..." ...>
        var serieMatches = Regex.Matches(html, @"<li\s+id=""([0-9A-Fa-f]{4})""\s+class=""([^""]*)""[^>]*>(.*?)(?=<li\s+id=""[0-9A-Fa-f]{4}""|</ul>\s*</div>\s*</div>)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (serieMatches.Count > 0)
        {
            foreach (Match sm in serieMatches)
            {
                string code = sm.Groups[1].Value;
                string classAttr = sm.Groups[2].Value;
                string body = sm.Groups[3].Value;

                bool isDisabled = classAttr.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                                  body.Contains("Продукт недоступен", StringComparison.OrdinalIgnoreCase) ||
                                  body.Contains("Срок действия истек", StringComparison.OrdinalIgnoreCase);

                var nameMatch = Regex.Match(body, @"<h2>.*?<span[^>]*>.*?</span>\s*([^<]+)</h2>", RegexOptions.Singleline);
                string name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : code;

                int keys = 0;
                int lics = 0;
                int inUse = 0;

                var keysMatch = Regex.Match(body, @"Всего\s*ключей:\s*<strong>(\d+)</strong>", RegexOptions.IgnoreCase);
                if (keysMatch.Success) int.TryParse(keysMatch.Groups[1].Value, out keys);

                var licsMatch = Regex.Match(body, @"Всего\s*лицензий:\s*<strong>(\d+)</strong>", RegexOptions.IgnoreCase);
                if (licsMatch.Success) int.TryParse(licsMatch.Groups[1].Value, out lics);

                var inUseMatch = Regex.Match(body, @"Использовано:\s*<strong>(\d+)</strong>", RegexOptions.IgnoreCase);
                if (inUseMatch.Success) int.TryParse(inUseMatch.Groups[1].Value, out inUse);

                // Запасной поиск из блока div.totals
                if (lics == 0)
                {
                    var totalDivs = Regex.Matches(body, @"<div\s+class=""total(?:\s+left|\s+right)?"">(\d+)</div>");
                    if (totalDivs.Count >= 3)
                    {
                        int.TryParse(totalDivs[0].Groups[1].Value, out keys);
                        int.TryParse(totalDivs[1].Groups[1].Value, out lics);
                        int.TryParse(totalDivs[2].Groups[1].Value, out inUse);
                    }
                }

                if (!isDisabled && lics > 0)
                {
                    products.Add(new SlkProductInfo(code, name, lics, inUse, Math.Max(0, lics - inUse)));
                    totalKeys += keys;
                    totalLicenses += lics;
                    inUseLicenses += inUse;

                    // Парсинг сеансов
                    var sessMatches = Regex.Matches(body, @"<li\s+class=""session[^""]*""[^>]*>(.*?)</li>", RegexOptions.Singleline);
                    foreach (Match s in sessMatches)
                    {
                        string sBody = s.Groups[1].Value;
                        var sidMatch = Regex.Match(sBody, @"Сеанс\s*№?\s*(\d+)", RegexOptions.IgnoreCase);
                        int? sid = sidMatch.Success ? int.Parse(sidMatch.Groups[1].Value) : null;

                        string uName = "";
                        var divMatches = Regex.Matches(sBody, @"<div>([^<]+)</div>");
                        foreach (Match d in divMatches)
                        {
                            string val = d.Groups[1].Value.Trim();
                            if (val.Contains(' ') && !val.Contains("Сеанс") && !val.Contains("клиент") && !val.Contains("Сервер"))
                            {
                                uName = val;
                            }
                        }

                        if (sid.HasValue || !string.IsNullOrWhiteSpace(uName))
                        {
                            activeSessions.Add(new SlkSessionHolder(
                                SessionId: sid,
                                ProductCode: code,
                                ProductName: name,
                                ClientHost: null,
                                ClientIp: null,
                                UserName: uName
                            ));
                        }
                    }
                }
            }
        }

        // 2. Fallback для упрощенного / текстового формата
        if (totalLicenses == 0)
        {
            var headerMatches = Regex.Matches(html, @"([0-9A-Fa-f]{4})\s+([^\d\r\n<]+?)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
            foreach (Match hm in headerMatches)
            {
                string code = hm.Groups[1].Value.Trim();
                string name = hm.Groups[2].Value.Trim();
                int.TryParse(hm.Groups[3].Value, out var k);
                int.TryParse(hm.Groups[4].Value, out var l);
                int.TryParse(hm.Groups[5].Value, out var u);

                if (l > 0)
                {
                    products.Add(new SlkProductInfo(code, name, l, u, Math.Max(0, l - u)));
                    totalKeys += k;
                    totalLicenses += l;
                    inUseLicenses += u;
                }
            }

            var inUseText = Regex.Match(html, @"(?:Использовано|Занято):\s*<b>?(\d+)</b>?", RegexOptions.IgnoreCase);
            if (inUseText.Success && int.TryParse(inUseText.Groups[1].Value, out var iu) && iu > 0)
            {
                inUseLicenses = iu;
            }
        }

        int freeLicenses = Math.Max(0, totalLicenses - inUseLicenses);
        return new SlkServerStatus(true, "Активен (СЛК 3.0)", totalKeys, totalLicenses, inUseLicenses, freeLicenses, products.AsReadOnly(), activeSessions.AsReadOnly(), "Подключено к веб-консоли СЛК 3.0");
    }

    public static SlkServerStatus? ParseSlkJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int total = 0;
            int inUse = 0;
            int totalKeys = 0;
            var products = new List<SlkProductInfo>();
            var activeSessions = new List<SlkSessionHolder>();

            if (root.TryGetProperty("totalLicenses", out var pTot)) total = pTot.GetInt32();
            if (root.TryGetProperty("inUseLicenses", out var pInUse)) inUse = pInUse.GetInt32();
            if (root.TryGetProperty("totalKeys", out var pKeys)) totalKeys = pKeys.GetInt32();

            return new SlkServerStatus(true, "Активен (API)", totalKeys, total, inUse, Math.Max(0, total - inUse), products.AsReadOnly(), activeSessions.AsReadOnly(), "Подключено через REST API СЛК");
        }
        catch
        {
            return null;
        }
    }
}