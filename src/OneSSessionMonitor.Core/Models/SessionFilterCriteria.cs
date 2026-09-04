using System;
using System.Collections.Generic;

namespace OneSSessionMonitor.Core.Models;

public sealed record SessionFilterCriteria(
    bool OnlyHibernate = true,
    TimeSpan? MinHibernateDuration = null,
    bool CleanFrozenSessions = true,
    int MaxDbProcMinutes = 5,
    int MaxCallDurationMinutes = 10,
    IReadOnlyList<string>? AppIds = null,
    string? InfoBaseNamePattern = null,
    string? UserNamePattern = null,
    IReadOnlyList<string>? ExcludedUsers = null,
    IReadOnlyList<string>? ExcludedInfoBases = null,
    IReadOnlyList<string>? ExcludedAppIds = null
)
{
    public static SessionFilterCriteria DefaultHibernateOnly => new(OnlyHibernate: true, CleanFrozenSessions: true, ExcludedAppIds: ["Designer"]);
}
