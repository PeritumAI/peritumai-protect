using System.Text.Json;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Events;

namespace Peritum.Protect.Core.Tests;

public class ActivityTimelineTests
{
    [Fact]
    public void Events_from_two_months_are_newest_first_with_one_heading_per_day()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-days-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var log = new EventLog(Path.Combine(dir, "events.jsonl"), new byte[32]);
            Write(dir, "events-2026-08.jsonl",
                Event(2026, 8, 15, 10, 59, "older-morning"),
                Event(2026, 8, 15, 13, 43, "older-afternoon"));
            Write(dir, "events-2026-09.jsonl",
                Event(2026, 9, 1, 1, 39, "today-early"),
                Event(2026, 8, 31, 22, 0, "yesterday"));

            var now = Local(2026, 9, 1, 15, 0);
            var recent = log.ReadRecent(200);
            Assert.Equal(
                ["today-early", "yesterday", "older-afternoon", "older-morning"],
                recent.Select(evt => evt.FileName));

            var lines = ActivityTimeline.Build(log.ReadAll(), now);
            Assert.Equal(["Today", "Yesterday", "15 August 2026"], lines.Where(line => line.ShowHeading).Select(line => line.Heading));
            Assert.Equal(["01:39", "22:00", "13:43", "10:59"], lines.Select(line => line.Time));
            Assert.False(lines[3].ShowHeading);
            Assert.Equal("", lines[3].Heading);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void Write(string dir, string name, params ProtectEvent[] events)
    {
        var text = string.Join("\n", events.Select(evt => JsonSerializer.Serialize(evt, JsonDefaults.Compact)));
        File.WriteAllText(Path.Combine(dir, name), text + "\n");
    }

    private static ProtectEvent Event(int year, int month, int day, int hour, int minute, string name) => new()
    {
        Ts = Local(year, month, day, hour, minute),
        Source = "manual",
        Action = EventActions.Warn,
        FileName = name
    };

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
