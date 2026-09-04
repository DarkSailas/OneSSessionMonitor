using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Services;
using OneSSessionMonitor.Core.State;

namespace OneSSessionMonitor.Service.Workers;

public sealed class SessionMonitorWorker(
    ISessionMonitorService cleanerService,
    IOptionsMonitor<SessionMonitorOptions> optionsMonitor,
    CleanerState cleanerState,
    ILogger<SessionMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        logger.LogInformation("Служба OneSSessionMonitor запущена. Опрос серверов через 1C RAS.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            var endpoints = options.GetEndpoints();
            var criteria = options.GetCriteria();
            int delaySec = Math.Max(10, options.IntervalSeconds);

            try
            {
                logger.LogInformation("Начало планового сканирования сеансов 1С на серверах: {Servers}",
                    string.Join(", ", endpoints.Select(e => e.DisplayAddress)));

                var report = await cleanerService.ExecuteCleanAsync(endpoints, criteria, options.DryRun, stoppingToken);

                cleanerState.RecordReport(report);

                logger.LogInformation("Сканирование завершено за {Ms} мс. Найдено сеансов: {Total}, Спящих: {Sleeping}, Отобрано: {Eligible}, Завершено: {Success}, Ошибок: {Errors}",
                    report.Duration.TotalMilliseconds,
                    report.TotalSessionsFound,
                    report.TotalSleepingSessions,
                    report.FilteredForTerminationCount,
                    report.SuccessfullyTerminatedCount,
                    report.FailedTerminationsCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Критическая ошибка при выполнении очистки сеансов 1С.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Служба OneSSessionMonitor остановлена.");
    }
}
