using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Tests;

public class PauseScheduleTests
{
    [Fact]
    public void Timed_pause_resumes_on_its_own_and_a_long_manual_pause_reminds_once()
    {
        var schedule = new PauseSchedule();
        var start = Local(2026, 9, 29, 14, 0);

        schedule.PauseFor(TimeSpan.FromMinutes(15), start);
        Assert.True(schedule.IsPaused(start));
        Assert.Equal("Paused until 14:15", schedule.Status(start));
        Assert.False(schedule.IsPaused(start.AddMinutes(15)));
        Assert.Equal("Protection on", schedule.Status(start.AddMinutes(15)));

        schedule.PauseFor(TimeSpan.FromHours(1), start);
        Assert.True(schedule.IsPaused(start.AddMinutes(59)));
        Assert.Equal("Paused until 15:00", schedule.Status(start));
        Assert.False(schedule.IsPaused(start.AddHours(1)));

        schedule.PauseUntilResume(start);
        Assert.Equal("Paused until I resume", schedule.Status(start.AddHours(7)));
        Assert.False(schedule.ShouldRemind(start.AddHours(7)));
        Assert.True(schedule.ShouldRemind(start.AddHours(8)));
        schedule.MarkReminded();
        Assert.False(schedule.ShouldRemind(start.AddHours(20)));
        schedule.Resume();
        Assert.False(schedule.IsPaused(start.AddHours(20)));
        Assert.Equal("Protection on", schedule.Status(start.AddHours(20)));
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
