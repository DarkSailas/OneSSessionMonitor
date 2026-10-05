using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.Ras;

public sealed class RasClient(
    RacProcessExecutor executor,
    ILogger<RasClient>? logger = null) : IRasClient
{
    private readonly ConcurrentDictionary<string, string> _infobaseNamesCache = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastWarmup = new();

    public async ValueTask<IReadOnlyList<V8ClusterInfo>> GetClustersAsync(OneCServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        string output = await executor.ExecuteRacAsync(BuildClusterListArguments(endpoint), endpoint.RacPath, cancellationToken);

        var clusters = new List<V8ClusterInfo>();
        var blocks = ParseRacBlocks(output);

        foreach (var block in blocks)
        {
            if (block.TryGetValue("cluster", out var clusterId) && block.TryGetValue("name", out var name))
            {
                clusters.Add(new V8ClusterInfo(
                    ClusterId: clusterId.Trim().Trim('"'),
                    ClusterName: name.Trim().Trim('"'),
                    Host: endpoint.Host,
                    Port: endpoint.ClusterPort,
                    AdminUser: endpoint.ClusterAdminUser,
                    AdminPassword: endpoint.ClusterAdminPassword
                ));
            }
        }

        return clusters;
    }

    public async ValueTask<IReadOnlyList<V8SessionInfo>> GetSessionsAsync(OneCServerEndpoint endpoint, V8ClusterInfo cluster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(cluster);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_lastWarmup.TryGetValue(cluster.ClusterId, out var lastTime) || DateTime.UtcNow - lastTime > TimeSpan.FromMinutes(15))
        {
            await WarmupInfobaseNamesAsync(endpoint, cluster, cancellationToken);
        }

        string output = await executor.ExecuteRacAsync(BuildSessionListArguments(endpoint, cluster), endpoint.RacPath, cancellationToken);

        var list = new List<V8SessionInfo>();
        var blocks = ParseRacBlocks(output);

        foreach (var block in blocks)
        {
            if (!block.TryGetValue("session-id", out var sidStr) || !int.TryParse(sidStr, out var sid))
            {
                continue;
            }

            block.TryGetValue("session", out var sessionUuid);
            block.TryGetValue("app-id", out var appId);
            string cleanAppId = (appId ?? string.Empty).Trim().Trim('"');

            if (string.Equals(cleanAppId, "SrvrConsole", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanAppId, "RAS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanAppId, "RAC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanAppId, "COMAdministrator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            block.TryGetValue("user-name", out var userName);
            block.TryGetValue("infobase", out var infoBaseId);
            block.TryGetValue("hibernate", out var hibStr);
            block.TryGetValue("hibernate-duration", out var hibDurationStr);
            block.TryGetValue("started-at", out var startedAtStr);
            block.TryGetValue("last-active-at", out var lastActiveStr);
            block.TryGetValue("host", out var host);
            block.TryGetValue("connection", out var connStr);
            block.TryGetValue("memory-current", out var memCurStr);
            block.TryGetValue("memory-total", out var memTotStr);
            block.TryGetValue("cpu-time-current", out var cpuCurStr);
            block.TryGetValue("cpu-time-total", out var cpuTotStr);
            block.TryGetValue("db-proc-took", out var dbProcStr);
            block.TryGetValue("call-duration-current", out var callCurStr);
            block.TryGetValue("call-duration-total", out var callTotStr);
            block.TryGetValue("blocked-by-dbms", out var blockedDbmsStr);
            block.TryGetValue("blocked-by-ls", out var blockedLsStr);
            block.TryGetValue("licenses", out var licStr);
            block.TryGetValue("license", out var singleLicStr);

            bool hibernate = string.Equals(hibStr, "yes", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(hibStr, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(hibStr, "1", StringComparison.OrdinalIgnoreCase);

            DateTime? startedAt = DateTime.TryParse(startedAtStr, CultureInfo.InvariantCulture, out var dtStart) ? dtStart : null;
            DateTime? lastActive = DateTime.TryParse(lastActiveStr, CultureInfo.InvariantCulture, out var dtLast) ? dtLast : null;
            long? hibDuration = long.TryParse(hibDurationStr, out var hd) ? hd : null;
            if (!hibDuration.HasValue && hibernate)
            {
                if (lastActive.HasValue)
                    hibDuration = (long)Math.Max(0, (DateTime.Now - lastActive.Value).TotalSeconds);
                else if (startedAt.HasValue)
                    hibDuration = (long)Math.Max(0, (DateTime.Now - startedAt.Value).TotalSeconds);
            }
            int? connId = int.TryParse(connStr, out var cid) ? cid : null;

            long? memoryBytes = long.TryParse(memCurStr, out var mc) && mc > 0 ? mc :
                               (long.TryParse(memTotStr, out var mt) && mt > 0 ? mt : null);

            long? cpuTimeMs = long.TryParse(cpuCurStr, out var cc) && cc > 0 ? cc :
                             (long.TryParse(cpuTotStr, out var ct) && ct > 0 ? ct : null);

            long? dbProcTookMs = long.TryParse(dbProcStr, out var dpt) ? dpt : null;
            long? callDurationMs = long.TryParse(callCurStr, out var ccMs) && ccMs > 0 ? ccMs :
                                  (long.TryParse(callTotStr, out var ctMs) && ctMs > 0 ? ctMs : null);

            bool blockedDbms = string.Equals(blockedDbmsStr, "yes", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(blockedDbmsStr, "1", StringComparison.OrdinalIgnoreCase);
            bool blockedLs = string.Equals(blockedLsStr, "yes", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(blockedLsStr, "1", StringComparison.OrdinalIgnoreCase);

            string? licenses = !string.IsNullOrWhiteSpace(licStr) ? licStr.Trim().Trim('"') : (singleLicStr?.Trim().Trim('"'));

            string cleanBaseId = (infoBaseId ?? string.Empty).Trim().Trim('"');
            string resolvedBaseName = cleanBaseId;
            if (!string.IsNullOrEmpty(cleanBaseId) && _infobaseNamesCache.TryGetValue(cleanBaseId, out var realName))
            {
                resolvedBaseName = realName;
            }

            bool isIndustryLic = false;
            if (!string.IsNullOrWhiteSpace(licenses))
            {
                isIndustryLic = licenses.Contains("СЛК", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("CRM", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("WMS", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("ERP", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("Itilium", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("Итилиум", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("Отрасл", StringComparison.OrdinalIgnoreCase) ||
                                licenses.Contains("Катран", StringComparison.OrdinalIgnoreCase);
            }



            list.Add(new V8SessionInfo(
                Server: endpoint.DisplayAddress,
                ClusterId: cluster.ClusterId,
                ClusterName: cluster.ClusterName.Trim().Trim('"'),
                SessionId: sid,
                UserName: (userName ?? string.Empty).Trim().Trim('"'),
                InfoBaseName: resolvedBaseName,
                AppId: cleanAppId,
                Hibernate: hibernate,
                SessionUuid: sessionUuid?.Trim().Trim('"'),
                HibernateDurationSeconds: hibDuration,
                StartedAt: startedAt,
                LastActiveAt: lastActive,
                Host: host?.Trim().Trim('"'),
                ConnectionId: connId,
                MemoryBytes: memoryBytes,
                CpuTimeMs: cpuTimeMs,
                DbProcTookMs: dbProcTookMs,
                CallDurationMs: callDurationMs,
                BlockedByDbms: blockedDbms,
                BlockedByLs: blockedLs,
                Licenses: licenses,
                HasIndustryLicense: isIndustryLic
            ));
        }

        return list;
    }

    private async ValueTask WarmupInfobaseNamesAsync(OneCServerEndpoint endpoint, V8ClusterInfo cluster, CancellationToken cancellationToken)
    {
        try
        {
            string output = await executor.ExecuteRacAsync(BuildInfobaseSummaryListArguments(endpoint, cluster), endpoint.RacPath, cancellationToken);

            var blocks = ParseRacBlocks(output);
            foreach (var block in blocks)
            {
                if (block.TryGetValue("infobase", out var id) && block.TryGetValue("name", out var name))
                {
                    _infobaseNamesCache[id.Trim().Trim('"')] = name.Trim().Trim('"');
                }
            }
            _lastWarmup[cluster.ClusterId] = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogDebug(ex, "Не удалось получить список баз данных кластера: {ClusterId}", cluster.ClusterId);
        }
    }

    public async ValueTask<bool> TerminateSessionAsync(OneCServerEndpoint endpoint, V8ClusterInfo cluster, V8SessionInfo session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await executor.ExecuteRacAsync(BuildSessionTerminateArguments(endpoint, cluster, session), endpoint.RacPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Не удалось завершить сеанс {SessionId} ({User})", session.SessionId, session.UserName);
            throw;
        }

        logger?.LogInformation("Успешно завершен сеанс {SessionId} ({User})", session.SessionId, session.UserName);
        return true;
    }

    public static IReadOnlyList<string> BuildClusterListArguments(OneCServerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return ["cluster", "list", FormatRasAddress(endpoint)];
    }

    public static IReadOnlyList<string> BuildSessionListArguments(OneCServerEndpoint endpoint, V8ClusterInfo cluster)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(cluster);

        var args = new List<string> { "session", "list", $"--cluster={cluster.ClusterId}" };
        AddClusterAuthArguments(args, cluster);
        args.Add(FormatRasAddress(endpoint));
        return args;
    }

    public static IReadOnlyList<string> BuildInfobaseSummaryListArguments(OneCServerEndpoint endpoint, V8ClusterInfo cluster)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(cluster);

        var args = new List<string> { "infobase", "summary", "list", $"--cluster={cluster.ClusterId}" };
        AddClusterAuthArguments(args, cluster);
        args.Add(FormatRasAddress(endpoint));
        return args;
    }

    /// <summary>
    /// Аргументы rac для завершения сеанса. rac принимает в --session только UUID сеанса,
    /// порядковый номер (session-id) приводит к «Ошибка разбора параметра: session».
    /// </summary>
    public static IReadOnlyList<string> BuildSessionTerminateArguments(OneCServerEndpoint endpoint, V8ClusterInfo cluster, V8SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(session);

        if (!Guid.TryParse(session.SessionUuid, out var sessionUuid))
        {
            throw new InvalidOperationException(
                $"У сеанса {session.SessionId} ({session.UserName}) нет идентификатора (UUID) — завершить его через rac нельзя. Обновите список сеансов и повторите.");
        }

        var args = new List<string> { "session", "terminate", $"--cluster={cluster.ClusterId}" };
        AddClusterAuthArguments(args, cluster);
        args.Add($"--session={sessionUuid:D}");
        args.Add(FormatRasAddress(endpoint));
        return args;
    }

    private static string FormatRasAddress(OneCServerEndpoint endpoint) => $"{endpoint.Host}:{endpoint.RasPort}";

    private static void AddClusterAuthArguments(List<string> args, V8ClusterInfo cluster)
    {
        if (string.IsNullOrWhiteSpace(cluster.AdminUser)) return;

        args.Add($"--cluster-user={cluster.AdminUser}");
        args.Add($"--cluster-pwd={cluster.AdminPassword ?? string.Empty}");
    }

    public static List<Dictionary<string, string>> ParseRacBlocks(string racOutput)
    {
        var result = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(racOutput)) return result;

        var lines = racOutput.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        Dictionary<string, string>? currentBlock = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                if (currentBlock != null && currentBlock.Count > 0)
                {
                    result.Add(currentBlock);
                    currentBlock = null;
                }
                continue;
            }

            int colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            string key = line[..colonIndex].Trim();
            string val = line[(colonIndex + 1)..].Trim();

            currentBlock ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            currentBlock[key] = val;
        }

        if (currentBlock != null && currentBlock.Count > 0)
        {
            result.Add(currentBlock);
        }

        return result;
    }
}