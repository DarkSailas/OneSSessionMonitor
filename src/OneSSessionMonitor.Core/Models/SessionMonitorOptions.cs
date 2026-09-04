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

    public IReadOnlyList<OneCServerEndpoint> GetEndpoints()
    {
        string srv = !string.IsNullOrWhiteSpace(Server) ? Server.Trim() : "localhost:1545";
        var ep = OneCServerEndpoint.Parse(srv) with
        {
            ClusterAdminUser = ClusterAdminUser,
            ClusterAdminPassword = ClusterAdminPassword,
            RacPath = RacPath
        };
        return [ep];
    }

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