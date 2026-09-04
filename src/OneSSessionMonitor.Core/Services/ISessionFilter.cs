using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.Services;

public interface ISessionFilter
{
    bool IsEligibleForTermination(V8SessionInfo session, SessionFilterCriteria criteria);
}
