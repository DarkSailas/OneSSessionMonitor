using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.State;

public sealed class CleanerState
{
    public DateTime? LastScanTime { get; set; }
    public CleanSummaryReport? LastReport { get; set; }
    public int LifetimeKilledSessions { get; set; }
    public List<TerminationResult> RecentKilledHistory { get; } = [];
    public readonly object SyncRoot = new();

    public void RecordReport(CleanSummaryReport report)
    {
        lock (SyncRoot)
        {
            LastScanTime = report.ExecutedAt;
            LastReport = report;
            if (!report.IsDryRun)
            {
                LifetimeKilledSessions += report.SuccessfullyTerminatedCount;
                foreach (var res in report.Results)
                {
                    if (res.Success)
                    {
                        RecentKilledHistory.Insert(0, res);
                    }
                }
                if (RecentKilledHistory.Count > 1000)
                {
                    RecentKilledHistory.RemoveRange(1000, RecentKilledHistory.Count - 1000);
                }
            }
        }
    }
}
