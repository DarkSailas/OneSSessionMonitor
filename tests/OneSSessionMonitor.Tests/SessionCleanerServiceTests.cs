using FluentAssertions;
using Moq;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Ras;
using OneSSessionMonitor.Core.Services;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class SessionCleanerServiceTests
{
    private readonly Mock<IRasClient> _rasMock = new();
    private readonly Mock<ISessionFilter> _filterMock = new();
    private readonly DefaultSessionMonitorService _service;

    public SessionCleanerServiceTests()
    {
        _service = new DefaultSessionMonitorService(_rasMock.Object, _filterMock.Object);
    }

    [Fact]
    public async Task ExecuteCleanAsync_ShouldDiscoverAndTerminateSessions_WhenEligible()
    {
        var server = new OneCServerEndpoint("srv-1c");
        var cluster = new V8ClusterInfo("c1", "MainCluster", "srv-1c", 1540);
        var activeSession = new V8SessionInfo("srv-1c", "c1", "MainCluster", 1, "Ivan", "Buh", "1CV8C", Hibernate: false);
        var deadSession = new V8SessionInfo("srv-1c", "c1", "MainCluster", 2, "Petr", "Buh", "1CV8C", Hibernate: true);

        _rasMock.Setup(x => x.GetClustersAsync(server, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cluster]);

        _rasMock.Setup(x => x.GetSessionsAsync(server, cluster, It.IsAny<CancellationToken>()))
            .ReturnsAsync([activeSession, deadSession]);

        _filterMock.Setup(x => x.IsEligibleForTermination(activeSession, It.IsAny<SessionFilterCriteria>())).Returns(false);
        _filterMock.Setup(x => x.IsEligibleForTermination(deadSession, It.IsAny<SessionFilterCriteria>())).Returns(true);

        _rasMock.Setup(x => x.TerminateSessionAsync(server, cluster, deadSession, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var report = await _service.ExecuteCleanAsync([server], SessionFilterCriteria.DefaultHibernateOnly, dryRun: false);

        report.TotalSessionsFound.Should().Be(2);
        report.TotalSleepingSessions.Should().Be(1);
        report.FilteredForTerminationCount.Should().Be(1);
        report.SuccessfullyTerminatedCount.Should().Be(1);
        report.FailedTerminationsCount.Should().Be(0);

        _rasMock.Verify(x => x.TerminateSessionAsync(server, cluster, deadSession, It.IsAny<CancellationToken>()), Times.Once);
        _rasMock.Verify(x => x.TerminateSessionAsync(server, cluster, activeSession, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCleanAsync_ShouldNotCallTerminate_InDryRunMode()
    {
        var server = new OneCServerEndpoint("srv-1c");
        var cluster = new V8ClusterInfo("c1", "MainCluster", "srv-1c", 1540);
        var deadSession = new V8SessionInfo("srv-1c", "c1", "MainCluster", 2, "Petr", "Buh", "1CV8C", Hibernate: true);

        _rasMock.Setup(x => x.GetClustersAsync(server, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cluster]);

        _rasMock.Setup(x => x.GetSessionsAsync(server, cluster, It.IsAny<CancellationToken>()))
            .ReturnsAsync([deadSession]);

        _filterMock.Setup(x => x.IsEligibleForTermination(deadSession, It.IsAny<SessionFilterCriteria>())).Returns(true);

        var report = await _service.ExecuteCleanAsync([server], SessionFilterCriteria.DefaultHibernateOnly, dryRun: true);

        report.IsDryRun.Should().BeTrue();
        report.SuccessfullyTerminatedCount.Should().Be(1);
        report.FailedTerminationsCount.Should().Be(0);

        _rasMock.Verify(x => x.TerminateSessionAsync(It.IsAny<OneCServerEndpoint>(), It.IsAny<V8ClusterInfo>(), It.IsAny<V8SessionInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
