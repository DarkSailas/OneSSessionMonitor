namespace OneSSessionMonitor.Core.Update;

/// <summary>Сведения о доступном обновлении из релиза GitHub.</summary>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string AssetName,
    Uri DownloadUrl,
    long SizeBytes,
    string? Sha256);
