using FluentAssertions;
using OneSSessionMonitor.Core.Models;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class CleanScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0);

    [Fact]
    public void GetNextRun_ShouldBeNull_WhenDisabled()
    {
        CleanSchedule.Create(false, "Interval", 30, "Minutes", "03:00").GetNextRun(Now).Should().BeNull();
    }

    [Fact]
    public void GetNextRun_ShouldAddMinutes_InIntervalMode()
    {
        CleanSchedule.Create(true, "Interval", 30, "Minutes", null).GetNextRun(Now)
            .Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public void GetNextRun_ShouldAddHours_InIntervalMode()
    {
        CleanSchedule.Create(true, "interval", 2, "hours", null).GetNextRun(Now)
            .Should().Be(Now.AddHours(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_ShouldClampIntervalToOneMinute(int value)
    {
        CleanSchedule.Create(true, "Interval", value, "Minutes", null).Interval
            .Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Create_ShouldNotOverflow_OnHugeInterval()
    {
        CleanSchedule.Create(true, "Interval", int.MaxValue, "Hours", null).Interval
            .Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public void GetNextRun_ShouldBeToday_WhenDailyTimeIsAhead()
    {
        CleanSchedule.Create(true, "Daily", 60, "Minutes", "18:30").GetNextRun(Now)
            .Should().Be(new DateTime(2026, 10, 4, 18, 30, 0));
    }

    [Theory]
    [InlineData("03:00")]
    [InlineData("12:00")]
    public void GetNextRun_ShouldBeTomorrow_WhenDailyTimeHasPassedOrIsNow(string time)
    {
        var next = CleanSchedule.Create(true, "Daily", 60, "Minutes", time).GetNextRun(Now);

        next.Should().Be(new DateTime(2026, 10, 5) + TimeOnly.Parse(time).ToTimeSpan());
    }

    [Theory]
    [InlineData("7:05", 7, 5)]
    [InlineData(" 23:59 ", 23, 59)]
    public void TryParseTime_ShouldAcceptHoursAndMinutes(string text, int hour, int minute)
    {
        CleanSchedule.TryParseTime(text, out var time).Should().BeTrue();
        time.Should().Be(new TimeOnly(hour, minute));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("утром")]
    public void Create_ShouldFallBackToDefaultTime_WhenTimeIsInvalid(string? text)
    {
        CleanSchedule.TryParseTime(text, out _).Should().BeFalse();
        CleanSchedule.Create(true, "Daily", 60, "Minutes", text).DailyTime
            .Should().Be(CleanSchedule.DefaultDailyTime);
    }

    [Fact]
    public void Options_GetSchedule_ShouldReflectSettings()
    {
        var options = new SessionMonitorOptions
        {
            ScheduleEnabled = true,
            ScheduleMode = "Daily",
            ScheduleDailyTime = "04:15"
        };

        options.GetSchedule().Should().Be(
            new CleanSchedule(true, CleanScheduleMode.Daily, TimeSpan.FromMinutes(60), new TimeOnly(4, 15)));
    }
}
