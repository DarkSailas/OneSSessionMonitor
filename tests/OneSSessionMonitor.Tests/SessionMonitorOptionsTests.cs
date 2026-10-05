using FluentAssertions;
using OneSSessionMonitor.Core.Models;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class SessionMonitorOptionsTests
{
    [Fact]
    public void GetEndpoints_ShouldReturnMainServerOnly_ByDefault()
    {
        var options = new SessionMonitorOptions { Server = "app01:9001" };

        options.GetEndpoints().Should().ContainSingle()
            .Which.Should().Match<OneCServerEndpoint>(e => e.Host == "app01" && e.RasPort == 9001);
    }

    [Fact]
    public void GetEndpoints_ShouldAppendAdditionalServers_WithSharedCredentials()
    {
        var options = new SessionMonitorOptions
        {
            Server = "app01",
            AdditionalServers = ["app02:9001", " app03 "],
            ClusterAdminUser = "admin",
            ClusterAdminPassword = "pwd",
            RacPath = @"C:\rac.exe"
        };

        var endpoints = options.GetEndpoints();

        endpoints.Select(e => $"{e.Host}:{e.RasPort}").Should().Equal("app01:1545", "app02:9001", "app03:1545");
        endpoints.Should().OnlyContain(e => e.ClusterAdminUser == "admin" && e.ClusterAdminPassword == "pwd" && e.RacPath == @"C:\rac.exe");
    }

    [Fact]
    public void GetEndpoints_ShouldSkipDuplicatesAndBlanks()
    {
        var options = new SessionMonitorOptions
        {
            Server = "app01",
            AdditionalServers = ["APP01:1545", "", "  ", "app02", "App02"]
        };

        options.GetEndpoints().Select(e => e.Host).Should().Equal("app01", "app02");
    }
}
