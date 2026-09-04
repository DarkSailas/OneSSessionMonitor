using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.Services;

public interface ISessionMonitorService
{
    IAsyncEnumerable<V8SessionInfo> DiscoverSessionsAsync(
        IReadOnlyList<OneCServerEndpoint> servers,
        CancellationToken cancellationToken = default);

    ValueTask<CleanSummaryReport> ExecuteCleanAsync(
        IReadOnlyList<OneCServerEndpoint> servers,
        SessionFilterCriteria criteria,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TerminateSingleSessionAsync(
        OneCServerEndpoint server,
        V8ClusterInfo cluster,
        V8SessionInfo session,
        CancellationToken cancellationToken = default);
}
