using System.Globalization;

namespace Peritum.Protect.Core.Events;

public sealed class ActivityEntry
{
    public bool ShowHeading { get; init; }
    public string Heading { get; init; } = "";
    public string Time { get; init; } = "";
    public required ProtectEvent Event { get; init; }
}

public static class ActivityTimeline
{
    public static IReadOnlyList<ActivityEntry> Build(IEnumerable<ProtectEvent> events, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var list = new List<ActivityEntry>();
        string? last = null;
        foreach (var evt in events.OrderByDescending(evt => evt.Ts))
        {
            var local = evt.Ts.ToLocalTime();
            var heading = Heading(DateOnly.FromDateTime(local.DateTime), today);
            var show = heading != last;
            list.Add(new ActivityEntry
            {
                ShowHeading = show,
                Heading = show ? heading : "",
                Time = local.ToString("HH:mm", CultureInfo.InvariantCulture),
                Event = evt
            });
            last = heading;
        }
        return list;
    }

    public static string Heading(DateOnly day, DateOnly today)
    {
        if (day == today)
            return "Today";
        if (day == today.AddDays(-1))
            return "Yesterday";
        return day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
    }
}
