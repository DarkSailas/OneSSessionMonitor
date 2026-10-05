using FluentAssertions;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Ras;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class RasClientArgumentsTests
{
    private const string ClusterId = "0e0d4c2a-6f0b-4d8e-9a3c-1b2c3d4e5f60";
    private const string SessionUuid = "7b1c9d2e-3f4a-4b5c-8d6e-9f0a1b2c3d4e";

    private static readonly OneCServerEndpoint Endpoint = new("app01", 9001);

    private static V8SessionInfo Session(string? uuid) =>
        new("app01:9001", ClusterId, "Main", 110, "Иванов", "Itilium", "1CV8C", true, uuid);

    [Fact]
    public void BuildClusterListArguments_ShouldTargetRasAddress()
    {
        RasClient.BuildClusterListArguments(Endpoint)
            .Should().Equal("cluster", "list", "app01:9001");
    }

    [Fact]
    public void BuildSessionTerminateArguments_ShouldPassSessionUuid_NotNumericId()
    {
        var cluster = new V8ClusterInfo(ClusterId, "Main", "app01", 1541);

        var args = RasClient.BuildSessionTerminateArguments(Endpoint, cluster, Session(SessionUuid));

        args.Should().Equal("session", "terminate", $"--cluster={ClusterId}", $"--session={SessionUuid}", "app01:9001");
        args.Should().NotContain("--session=110");
    }

    [Fact]
    public void BuildSessionTerminateArguments_ShouldAddClusterCredentials_AsSeparateArguments()
    {
        var cluster = new V8ClusterInfo(ClusterId, "Main", "app01", 1541, "admin", "p w\"d");

        var args = RasClient.BuildSessionTerminateArguments(Endpoint, cluster, Session(SessionUuid));

        args.Should().Contain("--cluster-user=admin");
        args.Should().Contain("--cluster-pwd=p w\"d");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("110")]
    public void BuildSessionTerminateArguments_ShouldThrow_WhenUuidIsMissingOrInvalid(string? uuid)
    {
        var cluster = new V8ClusterInfo(ClusterId, "Main", "app01", 1541);

        var act = () => RasClient.BuildSessionTerminateArguments(Endpoint, cluster, Session(uuid));

        act.Should().Throw<InvalidOperationException>().WithMessage("*UUID*");
    }

    [Fact]
    public void FormatArgumentsForLog_ShouldMaskClusterPassword()
    {
        var cluster = new V8ClusterInfo(ClusterId, "Main", "app01", 1541, "admin", "TopSecret");
        var args = RasClient.BuildSessionListArguments(Endpoint, cluster);

        string log = RacProcessExecutor.FormatArgumentsForLog(args);

        log.Should().NotContain("TopSecret");
        log.Should().Contain("--cluster-pwd=***");
    }
}
