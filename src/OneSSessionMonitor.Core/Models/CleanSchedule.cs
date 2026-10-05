using System.Globalization;

namespace OneSSessionMonitor.Core.Models;

public enum CleanScheduleMode
{
    // Раз в N минут / часов
    Interval,
    // Раз в сутки в указанное время
    Daily
}

/// <summary>Расписание автоматической очистки сеансов.</summary>
public sealed record CleanSchedule(bool Enabled, CleanScheduleMode Mode, TimeSpan Interval, TimeOnly DailyTime)
{
    public static readonly TimeOnly DefaultDailyTime = new(3, 0);

    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static CleanSchedule Create(bool enabled, string? mode, int intervalValue, string? intervalUnit, string? dailyTime)
    {
        var parsedMode = string.Equals(mode?.Trim(), nameof(CleanScheduleMode.Daily), StringComparison.OrdinalIgnoreCase)
            ? CleanScheduleMode.Daily
            : CleanScheduleMode.Interval;

        bool hours = string.Equals(intervalUnit?.Trim(), "Hours", StringComparison.OrdinalIgnoreCase);
        // Не меньше минуты и не больше 30 суток, чтобы мусорное значение не переполнило TimeSpan
        long minutes = Math.Clamp((long)intervalValue * (hours ? 60 : 1), 1, 30L * 24 * 60);

        var time = TryParseTime(dailyTime, out var parsed) ? parsed : DefaultDailyTime;
        return new CleanSchedule(enabled, parsedMode, TimeSpan.FromMinutes(minutes), time);
    }

    /// <summary>Ближайший запуск строго после <paramref name="from"/>; null, если расписание выключено.</summary>
    public DateTime? GetNextRun(DateTime from)
    {
        if (!Enabled) return null;

        if (Mode == CleanScheduleMode.Interval)
        {
            return from + Interval;
        }

        var today = from.Date + DailyTime.ToTimeSpan();
        return today > from ? today : today.AddDays(1);
    }
}
