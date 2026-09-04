using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Ras;

namespace OneSSessionMonitor.Core.Services;

public sealed class DefaultSessionMonitorService(
    IRasClient rasClient,
    ISessionFilter sessionFilter,
    ILogger<DefaultSessionMonitorService>? logger = null) : ISessionMonitorService
{
    public async IAsyncEnumerable<V8SessionInfo> DiscoverSessionsAsync(
        IReadOnlyList<OneCServerEndpoint> servers,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);

        foreach (var server in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<V8ClusterInfo> clusters;
            try
            {
                clusters = await rasClient.GetClustersAsync(server, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка получения списка кластеров с сервера '{Host}:{Port}' через RAS", server.Host, server.RasPort);
                continue;
            }

            foreach (var cluster in clusters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<V8SessionInfo> sessions;
                try
                {
                    sessions = await rasClient.GetSessionsAsync(server, cluster, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Ошибка получения сеансов кластера '{ClusterName}' на сервере '{Host}'", cluster.ClusterName, server.Host);
                    continue;
                }

                foreach (var session in sessions)
                {
                    yield return session;
                }
            }
        }
    }

    public async ValueTask<CleanSummaryReport> ExecuteCleanAsync(
        IReadOnlyList<OneCServerEndpoint> servers,
        SessionFilterCriteria criteria,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(criteria);

        var sw = Stopwatch.StartNew();
        int totalFound = 0;
        int totalSleeping = 0;
        var eligibleToKill = new List<(OneCServerEndpoint Server, V8ClusterInfo Cluster, V8SessionInfo Session)>();
        var results = new List<TerminationResult>();

        logger?.LogInformation("Начало сканирования сеансов 1С на {Count} серверах через RAS. Режим DryRun: {DryRun}",
            servers.Count, dryRun);

        foreach (var server in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<V8ClusterInfo> clusters;
            try
            {
                clusters = await rasClient.GetClustersAsync(server, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Не удалось подключиться к серверу RAS '{Host}:{Port}'", server.Host, server.RasPort);
                continue;
            }

            foreach (var cluster in clusters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<V8SessionInfo> sessions;
                try
                {
                    sessions = await rasClient.GetSessionsAsync(server, cluster, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Не удалось получить список сеансов кластера '{Cluster}'", cluster.ClusterName);
                    continue;
                }

                foreach (var session in sessions)
                {
                    totalFound++;
                    if (session.Hibernate) totalSleeping++;

                    if (sessionFilter.IsEligibleForTermination(session, criteria))
                    {
                        eligibleToKill.Add((server, cluster, session));
                    }
                }
            }
        }

        int successCount = 0;
        int failCount = 0;

        foreach (var (server, cluster, session) in eligibleToKill)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (dryRun)
            {
                results.Add(new TerminationResult(session, Success: true, ErrorMessage: "[DRY-RUN] Симуляция завершения"));
                successCount++;
                continue;
            }

            try
            {
                bool ok = await rasClient.TerminateSessionAsync(server, cluster, session, cancellationToken);
                if (ok)
                {
                    successCount++;
                    results.Add(new TerminationResult(session, Success: true));
                }
                else
                {
                    failCount++;
                    results.Add(new TerminationResult(session, Success: false, ErrorMessage: "Сервер RAS вернул ошибку при завершении сеанса."));
                }
            }
            catch (Exception ex)
            {
                failCount++;
                logger?.LogError(ex, "Исключение при завершении сеанса {SessionId} (Пользователь: '{User}')", session.SessionId, session.UserName);
                results.Add(new TerminationResult(session, Success: false, ErrorMessage: ex.Message));
            }
        }

        sw.Stop();

        var report = new CleanSummaryReport(
            Servers: servers,
            TotalSessionsFound: totalFound,
            TotalSleepingSessions: totalSleeping,
            FilteredForTerminationCount: eligibleToKill.Count,
            SuccessfullyTerminatedCount: successCount,
            FailedTerminationsCount: failCount,
            Results: results.AsReadOnly(),
            Duration: sw.Elapsed,
            IsDryRun: dryRun,
            ExecutedAt: DateTime.Now
        );

        logger?.LogInformation("Очистка сеансов завершена за {ElapsedMs} мс. Найдено сеансов: {Total}, Спящих: {Sleeping}, Отобрано: {Eligible}, Завершено: {Success}, Ошибок: {Fail}",
            sw.ElapsedMilliseconds, totalFound, totalSleeping, eligibleToKill.Count, successCount, failCount);

        return report;
    }

    public ValueTask<bool> TerminateSingleSessionAsync(OneCServerEndpoint server, V8ClusterInfo cluster, V8SessionInfo session, CancellationToken cancellationToken = default)
    {
        return rasClient.TerminateSessionAsync(server, cluster, session, cancellationToken);
    }
}
