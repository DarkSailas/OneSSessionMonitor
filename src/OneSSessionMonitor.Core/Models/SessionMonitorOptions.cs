using System;
using System.Collections.Generic;

namespace OneSSessionMonitor.Core.Models;

public sealed class SessionMonitorOptions
{
    public const string SectionName = "SessionMonitor";

    // Единственный сервер администрирования 1С (RAS) в формате "хост:порт"
    public string Server { get; set; } = "localhost:1545";
    public string? RacPath { get; set; }
    public string? ClusterAdminUser { get; set; }
    public string? ClusterAdminPassword { get; set; }

    // Параметры спящих сеансов
    public bool OnlyHibernate { get; set; } = true;
    public int MinHibernateMinutes { get; set; } = 90;

    // Параметры зависших сеансов
    public bool CleanFrozenSessions { get; set; } = true;
    public int MaxDbProcMinutes { get; set; } = 40;
    public int MaxCallDurationMinutes { get; set; } = 30;

    // Белые списки и фильтры
    public List<string> ExcludedUsers { get; set; } = [];
    public List<string> ExcludedInfoBases { get; set; } = [];
    public List<string> ExcludedAppIds { get; set; } = [];
    public List<string> TargetAppIds { get; set; } = [];
    public string? InfoBasePattern { get; set; }
    public string? UserNamePattern { get; set; }

    // Единственный сервер защиты СЛК 3.0 / 2.0 (хост:порт)
    public string? SlkServerEndpoint { get; set; } = "localhost:9099";
    public string? SlkUser { get; set; }
    public string? SlkPassword { get; set; }

    // Служба и логирование
    public int IntervalSeconds { get; set; } = 60;
    public bool DryRun { get; set; } = false;
    public int LogRetainedDays { get; set; } = 7;
    public int LogMaxTotalSizeMb { get; set; } = 100;
    public string LogLevel { get; set; } = "Error";

    // Дополнительные серверы 1С (host или host:порт RAS), опрашиваются вместе с основным
    public List<string> AdditionalServers { get; set; } = [];

    // Автоматическая очистка по расписанию
    public bool ScheduleEnabled { get; set; } = false;
    // "Interval" — раз в N минут/часов, "Daily" — раз в сутки в указанное время
    public string ScheduleMode { get; set; } = "Interval";
    public int ScheduleIntervalValue { get; set; } = 60;
    // "Minutes" или "Hours"
    public string ScheduleIntervalUnit { get; set; } = "Minutes";
    // Время запуска в режиме "Daily" (ЧЧ:ММ)
    public string ScheduleDailyTime { get; set; } = "03:00";

    // Проверять обновления GUI при запуске
    public bool AutoUpdateEnabled { get; set; } = true;

    public IReadOnlyList<OneCServerEndpoint> GetEndpoints()
    {
        string srv = !string.IsNullOrWhiteSpace(Server) ? Server.Trim() : "localhost:1545";

        var result = new List<OneCServerEndpoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string address in new[] { srv }.Concat(AdditionalServers ?? []))
        {
            if (string.IsNullOrWhiteSpace(address)) continue;

            var ep = OneCServerEndpoint.Parse(address.Trim()) with
            {
                ClusterAdminUser = ClusterAdminUser,
                ClusterAdminPassword = ClusterAdminPassword,
                RacPath = RacPath
            };

            if (seen.Add($"{ep.Host}:{ep.RasPort}"))
            {
                result.Add(ep);
            }
        }

        return result;
    }

    public CleanSchedule GetSchedule() =>
        CleanSchedule.Create(ScheduleEnabled, ScheduleMode, ScheduleIntervalValue, ScheduleIntervalUnit, ScheduleDailyTime);

    public SessionFilterCriteria GetCriteria()
    {
        var excludedUsers = ExcludedUsers.Count > 0 
            ? ExcludedUsers.Distinct(StringComparer.OrdinalIgnoreCase).ToList() 
            : ["Administrator", "Администратор", "DefUser", "ServiceExchange", "ФоновыйОбмен"];

        var excludedApps = ExcludedAppIds.Count > 0
            ? ExcludedAppIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : ["Designer"];

        return new SessionFilterCriteria(
            OnlyHibernate: OnlyHibernate,
            MinHibernateDuration: TimeSpan.FromMinutes(MinHibernateMinutes),
            CleanFrozenSessions: CleanFrozenSessions,
            MaxDbProcMinutes: MaxDbProcMinutes,
            MaxCallDurationMinutes: MaxCallDurationMinutes,
            AppIds: TargetAppIds,
            InfoBaseNamePattern: InfoBasePattern,
            UserNamePattern: UserNamePattern,
            ExcludedUsers: excludedUsers,
            ExcludedInfoBases: ExcludedInfoBases,
            ExcludedAppIds: excludedApps
        );
    }
}