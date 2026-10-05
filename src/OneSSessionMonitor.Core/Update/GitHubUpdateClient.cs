using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace OneSSessionMonitor.Core.Update;

/// <summary>Проверка и загрузка обновлений из релизов GitHub.</summary>
public sealed class GitHubUpdateClient : IDisposable
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/DarkSailas/OneSSessionMonitor/releases/latest";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;

    public GitHubUpdateClient() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
    {
    }

    public GitHubUpdateClient(HttpClient http)
    {
        _http = http;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OneSSessionMonitor", "1.0"));
        }
    }

    /// <summary>Возвращает сведения об обновлении или null, если установлена актуальная версия либо релизов нет.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // У репозитория ещё нет опубликованных релизов
            return null;
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return GitHubReleaseParser.Parse(json, current);
    }

    /// <summary>Скачивает архив обновления в <paramref name="destinationPath"/> и сверяет размер и SHA-256.</summary>
    public async Task DownloadAsync(UpdateInfo update, string destinationPath, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > update.SizeBytes)
                {
                    throw new InvalidDataException("Размер архива обновления больше заявленного в релизе.");
                }

                sha.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }

        if (total != update.SizeBytes)
        {
            throw new InvalidDataException("Архив обновления скачан не полностью.");
        }

        if (update.Sha256 is not null)
        {
            string actual = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (!string.Equals(actual, update.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Контрольная сумма SHA-256 архива обновления не совпала с указанной в релизе.");
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
