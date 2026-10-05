using System.Text.Json;

namespace OneSSessionMonitor.Core.Update;

public static class GitHubReleaseParser
{
    // Архив больше этого размера не скачивается
    public const long MaxAssetSizeBytes = 300L * 1024 * 1024;

    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string text = tag.Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0) text = text[..suffix];

        if (!Version.TryParse(text, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    /// <summary>
    /// Разбирает ответ GitHub API "releases/latest". Возвращает обновление, только если релиз новее
    /// <paramref name="current"/> и содержит zip-архив GUI со ссылкой на github.com.
    /// </summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

        string? tag = GetString(root, "tag_name");
        if (tag is null || !TryParseVersion(tag, out var version) || version <= Normalize(current)) return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;

            string? name = GetString(asset, "name");
            string? url = GetString(asset, "browser_download_url");
            if (name is null || url is null) continue;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains("gui", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryGetTrustedDownloadUri(url, out var uri)) continue;

            long size = asset.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt64(out var s) ? s : 0;
            if (size is <= 0 or > MaxAssetSizeBytes) continue;

            // Без контрольной суммы целостность архива не проверить — такой файл не устанавливаем
            string? sha256 = ParseSha256(GetString(asset, "digest"));
            if (sha256 is null) continue;

            return new UpdateInfo(version, tag, name, uri, size, sha256);
        }

        return null;
    }

    private const string ReleaseDownloadPath = "/DarkSailas/OneSSessionMonitor/releases/download/";

    public static bool TryGetTrustedDownloadUri(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && parsed.AbsolutePath.StartsWith(ReleaseDownloadPath, StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = new Uri("https://github.com/");
        return false;
    }

    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        string hex = digest[prefix.Length..].Trim();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
