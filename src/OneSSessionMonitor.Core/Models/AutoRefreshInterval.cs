using System.Globalization;

namespace OneSSessionMonitor.Core.Models;

/// <summary>
/// Интервал автообновления списка сеансов и лицензий в окне программы.
/// </summary>
public static class AutoRefreshInterval
{
    // Полный опрос кластера через rac заметно тяжелее запроса к СЛК, поэтому чаще 10 секунд не опрашиваем
    public const int MinSeconds = 10;
    public const int MaxSeconds = 86400;
    public const int DefaultSeconds = 60;

    /// <summary>
    /// Интервал автообновления или null, если оно выключено. Значение вне допустимых границ приводится к ближайшей.
    /// </summary>
    public static TimeSpan? Get(bool enabled, int seconds)
    {
        if (!enabled) return null;

        return TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds));
    }

    public static bool TryParseSeconds(string? text, out int seconds)
    {
        if (int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value is >= MinSeconds and <= MaxSeconds)
        {
            seconds = value;
            return true;
        }

        seconds = 0;
        return false;
    }
}
