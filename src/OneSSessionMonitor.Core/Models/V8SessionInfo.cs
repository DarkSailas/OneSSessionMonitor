using System;
using System.Globalization;

namespace OneSSessionMonitor.Core.Models;

public record V8SessionInfo(
    string Server,
    string ClusterId,
    string ClusterName,
    int SessionId,
    string UserName,
    string InfoBaseName,
    string AppId,
    bool Hibernate,
    string? SessionUuid = null,
    long? HibernateDurationSeconds = null,
    DateTime? StartedAt = null,
    DateTime? LastActiveAt = null,
    string? Host = null,
    int? ConnectionId = null,
    long? MemoryBytes = null,
    long? CpuTimeMs = null,
    long? DbProcTookMs = null,
    long? CallDurationMs = null,
    bool BlockedByDbms = false,
    bool BlockedByLs = false,
    string? Licenses = null,
    bool HasIndustryLicense = false
)
{
    public bool IsFrozen => BlockedByDbms || BlockedByLs || (DbProcTookMs.HasValue && DbProcTookMs.Value > 300_000) || (CallDurationMs.HasValue && CallDurationMs.Value > 600_000);

    public int StatusSortOrder => IsFrozen ? 0 : (Hibernate ? 1 : 2);

    public string StatusText => IsFrozen ? "ЗАВИСШИЙ" : (Hibernate ? "СПЯЩИЙ" : "АКТИВЕН");

    public long SortMemoryBytes => MemoryBytes ?? 0;

    public long SortCpuTimeMs => CpuTimeMs ?? 0;

    public DateTime SortStartedAt => StartedAt ?? DateTime.MinValue;

    public double InactiveDurationSeconds
    {
        get
        {
            if (IsFrozen)
            {
                return 100_000_000.0 + (DbProcTookMs ?? (CallDurationMs ?? 0)) / 1000.0;
            }

            if (!Hibernate) return 0.0;

            if (HibernateDurationSeconds.HasValue && HibernateDurationSeconds.Value > 0)
                return HibernateDurationSeconds.Value;

            if (LastActiveAt.HasValue)
                return Math.Max(0, (DateTime.Now - LastActiveAt.Value).TotalSeconds);

            if (StartedAt.HasValue)
                return Math.Max(0, (DateTime.Now - StartedAt.Value).TotalSeconds);

            return 0.0;
        }
    }

    public string FormattedMemory
    {
        get
        {
            if (!MemoryBytes.HasValue || MemoryBytes.Value <= 0) return "—";
            double mb = MemoryBytes.Value / (1024.0 * 1024.0);
            return mb >= 1024.0 ? $"{mb / 1024.0:F2} ГБ" : $"{mb:F1} МБ";
        }
    }

    public string FormattedCpuTime
    {
        get
        {
            if (!CpuTimeMs.HasValue || CpuTimeMs.Value <= 0) return "0.0% (0.0 с)";
            double sec = CpuTimeMs.Value / 1000.0;
            double activePct = (Hibernate || IsFrozen) ? 0.0 : Math.Min(100.0, Math.Round((sec / Math.Max(1.0, (DateTime.Now - (StartedAt ?? DateTime.Now)).TotalSeconds)) * 100.0 * 8, 1));
            return $"{activePct:F1}% ({sec:F1} с)";
        }
    }

    public string FormattedLicenses
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Licenses))
            {
                return HasIndustryLicense ? "СЛК Лицензия" : "Платформенная 1С";
            }
            return Licenses;
        }
    }

    public string FormattedHibernateDuration
    {
        get
        {
            if (IsFrozen)
            {
                if (BlockedByDbms) return "Блок СУБД";
                if (BlockedByLs) return "Блок 1С Сервера";
                if (DbProcTookMs.HasValue) return $"СУБД: {DbProcTookMs.Value / 1000} с";
                if (CallDurationMs.HasValue) return $"Вызов: {CallDurationMs.Value / 1000} с";
            }

            if (!Hibernate) return "Активен";

            double durSec = InactiveDurationSeconds;
            var dur = TimeSpan.FromSeconds(durSec);
            if (dur.TotalHours >= 1) return $"Спит ({(int)dur.TotalHours}ч {dur.Minutes}м)";
            if (dur.TotalMinutes >= 1) return $"Спит ({dur.Minutes}м {dur.Seconds}с)";
            return $"Спит ({Math.Max(1, dur.Seconds)}с)";
        }
    }

    public string FormattedStartedAt => StartedAt?.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";
}