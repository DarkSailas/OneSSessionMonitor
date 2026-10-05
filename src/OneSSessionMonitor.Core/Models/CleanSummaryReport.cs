namespace OneSSessionMonitor.Core.Models;

public sealed record CleanSummaryReport(
    IReadOnlyList<OneCServerEndpoint> Servers,
    int TotalSessionsFound,
    int TotalSleepingSessions,
    int FilteredForTerminationCount,
    int SuccessfullyTerminatedCount,
    int FailedTerminationsCount,
    IReadOnlyList<TerminationResult> Results,
    TimeSpan Duration,
    bool IsDryRun,
    DateTime ExecutedAt
);
