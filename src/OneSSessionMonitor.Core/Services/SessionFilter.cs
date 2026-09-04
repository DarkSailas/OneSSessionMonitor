using System;
using System.Text.RegularExpressions;
using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.Services;

public sealed class SessionFilter : ISessionFilter
{
    public bool IsEligibleForTermination(V8SessionInfo session, SessionFilterCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(criteria);

        // 1. Белый список пользователей (Никогда не завершать)
        if (criteria.ExcludedUsers is { Count: > 0 })
        {
            foreach (var excluded in criteria.ExcludedUsers)
            {
                if (string.Equals(session.UserName, excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // 2. Белый список информационных баз (Никогда не завершать)
        if (criteria.ExcludedInfoBases is { Count: > 0 })
        {
            foreach (var excluded in criteria.ExcludedInfoBases)
            {
                if (string.Equals(session.InfoBaseName, excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // 3. Белый список типов приложений (Никогда не завершать Конфигуратор и исключенные AppID)
        if (criteria.ExcludedAppIds is { Count: > 0 })
        {
            foreach (var excluded in criteria.ExcludedAppIds)
            {
                if (string.Equals(session.AppId, excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        else if (string.Equals(session.AppId, "Designer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 4. Проверка признака зависания (Frozen)
        bool isFrozenEligible = false;
        if (criteria.CleanFrozenSessions)
        {
            if (session.BlockedByDbms || session.BlockedByLs)
            {
                isFrozenEligible = true;
            }
            else if (session.DbProcTookMs.HasValue && session.DbProcTookMs.Value >= criteria.MaxDbProcMinutes * 60_000)
            {
                isFrozenEligible = true;
            }
            else if (session.CallDurationMs.HasValue && session.CallDurationMs.Value >= criteria.MaxCallDurationMinutes * 60_000)
            {
                isFrozenEligible = true;
            }
        }

        // 4. Проверка признака сна (Hibernate)
        bool isSleepingEligible = false;
        if (session.Hibernate)
        {
            if (!criteria.MinHibernateDuration.HasValue || criteria.MinHibernateDuration.Value.TotalSeconds <= 0)
            {
                isSleepingEligible = true;
            }
            else
            {
                double durationSec = session.HibernateDurationSeconds ?? 
                    (session.LastActiveAt.HasValue ? (DateTime.Now - session.LastActiveAt.Value).TotalSeconds : 
                    (session.StartedAt.HasValue ? (DateTime.Now - session.StartedAt.Value).TotalSeconds : 0));

                if (durationSec >= criteria.MinHibernateDuration.Value.TotalSeconds)
                {
                    isSleepingEligible = true;
                }
            }
        }

        bool eligible = isFrozenEligible || (criteria.OnlyHibernate ? isSleepingEligible : (isSleepingEligible || isFrozenEligible));
        if (!eligible)
        {
            return false;
        }

        // 5. Regex фильтр имени пользователя
        if (!string.IsNullOrWhiteSpace(criteria.UserNamePattern))
        {
            if (!Regex.IsMatch(session.UserName ?? string.Empty, criteria.UserNamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        // 6. Regex фильтр имени базы
        if (!string.IsNullOrWhiteSpace(criteria.InfoBaseNamePattern))
        {
            if (!Regex.IsMatch(session.InfoBaseName ?? string.Empty, criteria.InfoBaseNamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        // 7. Список AppId
        if (criteria.AppIds is { Count: > 0 })
        {
            bool appMatched = false;
            foreach (var app in criteria.AppIds)
            {
                if (string.Equals(session.AppId, app, StringComparison.OrdinalIgnoreCase))
                {
                    appMatched = true;
                    break;
                }
            }
            if (!appMatched)
            {
                return false;
            }
        }

        return true;
    }
}