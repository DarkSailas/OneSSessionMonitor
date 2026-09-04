using FluentAssertions;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Services;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class SessionFilterTests
{
    private readonly SessionFilter _filter = new();

    [Fact]
    public void IsEligibleForTermination_ShouldReturnFalse_WhenSessionIsNotHibernateAndOnlyHibernateIsTrue()
    {
        var session = new V8SessionInfo("srv", "c1", "Cluster1", 101, "User1", "Base1", "1CV8C", Hibernate: false);
        var criteria = new SessionFilterCriteria(OnlyHibernate: true);

        bool result = _filter.IsEligibleForTermination(session, criteria);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldReturnTrue_WhenSessionIsHibernateAndCriteriaMatches()
    {
        var session = new V8SessionInfo("srv", "c1", "Cluster1", 101, "User1", "Base1", "1CV8C", Hibernate: true);
        var criteria = new SessionFilterCriteria(OnlyHibernate: true);

        bool result = _filter.IsEligibleForTermination(session, criteria);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldReturnFalse_WhenUserIsExcluded()
    {
        var session = new V8SessionInfo("srv", "c1", "Cluster1", 101, "Administrator", "Base1", "1CV8C", Hibernate: true);
        var criteria = new SessionFilterCriteria(OnlyHibernate: true, ExcludedUsers: ["administrator", "defuser"]);

        bool result = _filter.IsEligibleForTermination(session, criteria);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldReturnFalse_WhenInfoBaseIsExcluded()
    {
        var session = new V8SessionInfo("srv", "c1", "Cluster1", 101, "User1", "ProductionDB", "1CV8C", Hibernate: true);
        var criteria = new SessionFilterCriteria(OnlyHibernate: true, ExcludedInfoBases: ["productiondb"]);

        bool result = _filter.IsEligibleForTermination(session, criteria);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldMatch_RegexForUserName()
    {
        var session1 = new V8SessionInfo("srv", "c1", "Cluster1", 101, "Service_1", "Base1", "1CV8C", Hibernate: true);
        var session2 = new V8SessionInfo("srv", "c1", "Cluster1", 102, "User_1", "Base1", "1CV8C", Hibernate: true);

        var criteria = new SessionFilterCriteria(OnlyHibernate: true, UserNamePattern: "^Service_.*");

        _filter.IsEligibleForTermination(session1, criteria).Should().BeTrue();
        _filter.IsEligibleForTermination(session2, criteria).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldRespect_MinHibernateDuration()
    {
        var session1 = new V8SessionInfo("srv", "c1", "Cluster1", 101, "User1", "Base1", "1CV8C", Hibernate: true, HibernateDurationSeconds: 120);
        var session2 = new V8SessionInfo("srv", "c1", "Cluster1", 102, "User2", "Base1", "1CV8C", Hibernate: true, HibernateDurationSeconds: 700);

        var criteria = new SessionFilterCriteria(OnlyHibernate: true, MinHibernateDuration: TimeSpan.FromMinutes(10)); // 600s

        _filter.IsEligibleForTermination(session1, criteria).Should().BeFalse();
        _filter.IsEligibleForTermination(session2, criteria).Should().BeTrue();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldReturnFalse_WhenAppIsDesignerByDefault()
    {
        var designerSession = new V8SessionInfo("srv", "c1", "Cluster1", 101, "Developer", "Base1", "Designer", Hibernate: true, HibernateDurationSeconds: 10000);
        var criteria = new SessionFilterCriteria(OnlyHibernate: true);

        bool result = _filter.IsEligibleForTermination(designerSession, criteria);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForTermination_ShouldReturnFalse_WhenAppIsInExcludedAppIds()
    {
        var designerSession = new V8SessionInfo("srv", "c1", "Cluster1", 101, "Developer", "Base1", "designer", Hibernate: true, HibernateDurationSeconds: 10000);
        var criteria = new SessionFilterCriteria(OnlyHibernate: true, ExcludedAppIds: ["Designer", "COMConnector"]);

        bool result = _filter.IsEligibleForTermination(designerSession, criteria);

        result.Should().BeFalse();
    }
}
