using FluentAssertions;
using OneSSessionMonitor.Core.Models;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class AutoRefreshIntervalTests
{
    [Fact]
    public void Get_ShouldReturnConfiguredInterval()
    {
        AutoRefreshInterval.Get(enabled: true, seconds: 30).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Get_ShouldBeNull_WhenDisabled()
    {
        AutoRefreshInterval.Get(enabled: false, seconds: 30).Should().BeNull();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-10, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(100000, 86400)]
    public void Get_ShouldClampInterval(int configured, int expectedSeconds)
    {
        AutoRefreshInterval.Get(enabled: true, configured).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("60", true, 60)]
    [InlineData(" 15 ", true, 15)]
    [InlineData("10", true, 10)]
    [InlineData("86400", true, 86400)]
    [InlineData("9", false, 0)]
    [InlineData("86401", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParseSeconds_ShouldAcceptOnlyValuesInRange(string? text, bool expected, int expectedSeconds)
    {
        AutoRefreshInterval.TryParseSeconds(text, out int seconds).Should().Be(expected);
        seconds.Should().Be(expectedSeconds);
    }
}
