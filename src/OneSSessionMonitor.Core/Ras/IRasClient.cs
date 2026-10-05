using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.Ras;

public interface IRasClient
{
    ValueTask<IReadOnlyList<V8ClusterInfo>> GetClustersAsync(OneCServerEndpoint endpoint, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<V8SessionInfo>> GetSessionsAsync(OneCServerEndpoint endpoint, V8ClusterInfo cluster, CancellationToken cancellationToken = default);
    ValueTask<bool> TerminateSessionAsync(OneCServerEndpoint endpoint, V8ClusterInfo cluster, V8SessionInfo session, CancellationToken cancellationToken = default);
}
