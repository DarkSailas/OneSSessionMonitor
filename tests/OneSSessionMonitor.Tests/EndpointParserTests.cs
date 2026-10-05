using FluentAssertions;
using OneSSessionMonitor.Core.Models;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class EndpointParserTests
{
    [Fact]
    public void Parse_ShouldParseHostOnly_WithDefaultRasPort()
    {
        var ep = OneCServerEndpoint.Parse("srv-1c-central");

        ep.Host.Should().Be("srv-1c-central");
        ep.RasPort.Should().Be(1545);
    }

    [Fact]
    public void Parse_ShouldParseHostAndCustomPort()
    {
        var ep = OneCServerEndpoint.Parse("192.168.1.50:2545");

        ep.Host.Should().Be("192.168.1.50");
        ep.RasPort.Should().Be(2545);
    }
}
