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

        CleanSchedule? activeSchedule = null;
        DateTime nextRun = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            var schedule = ResolveSchedule(options);

            if (schedule != activeSchedule)
            {
                // Первый проход или настройки расписания изменились в appsettings.json
                // Сразу запускаемся только при старте службы в интервальном режиме;
                // смена настроек на ходу не должна вызывать внеочередную очистку
                bool runNow = activeSchedule is null && schedule.Mode == CleanScheduleMode.Interval;
                activeSchedule = schedule;
                nextRun = runNow
                    ? DateTime.Now
                    : schedule.GetNextRun(DateTime.Now) ?? DateTime.Now + schedule.Interval;

                if (schedule.Mode == CleanScheduleMode.Daily)
                {
                    logger.LogInformation("Расписание очистки: ежедневно в {Time}. Следующий запуск: {NextRun}", schedule.DailyTime, nextRun);
                }
                else
                {
                    logger.LogInformation("Расписание очистки: каждые {Interval}", schedule.Interval);
                }
            }

            if (DateTime.Now >= nextRun)
            {
                try
                {
                    var endpoints = options.GetEndpoints();
                    var criteria = options.GetCriteria();

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

                nextRun = schedule.GetNextRun(DateTime.Now) ?? DateTime.Now + schedule.Interval;
            }

            // Короткое ожидание, чтобы изменения расписания подхватывались без перезапуска службы
            var delay = nextRun - DateTime.Now;
            if (delay < MinDelay) delay = MinDelay;
            if (delay > MaxDelay) delay = MaxDelay;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Служба OneSSessionMonitor остановлена.");
    }

    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    // Без включённого расписания служба работает как раньше: опрос каждые IntervalSeconds
    private static CleanSchedule ResolveSchedule(SessionMonitorOptions options) =>
        options.ScheduleEnabled
            ? options.GetSchedule()
            : new CleanSchedule(true, CleanScheduleMode.Interval, TimeSpan.FromSeconds(Math.Max(10, options.IntervalSeconds)), default);
}
