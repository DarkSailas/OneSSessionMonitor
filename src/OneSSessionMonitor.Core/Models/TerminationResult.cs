namespace OneSSessionMonitor.Core.Models;

public sealed record TerminationResult(
    V8SessionInfo Session,
    bool Success,
    string? ErrorMessage = null,
    DateTime Timestamp = default
)
{
    public TerminationResult(V8SessionInfo session, bool success, string? errorMessage = null)
        : this(session, success, errorMessage, DateTime.Now) { }
}
