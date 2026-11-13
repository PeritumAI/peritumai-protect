using System.Globalization;

namespace Peritum.Protect.Core;

public sealed class PauseSchedule
{
    public DateTimeOffset? ResumeAt { get; private set; }
    public DateTimeOffset? PausedAt { get; private set; }
    public bool UntilResume { get; private set; }
    public bool ReminderShown { get; private set; }

    public void PauseFor(TimeSpan duration, DateTimeOffset now)
    {
        UntilResume = false;
        ReminderShown = false;
        PausedAt = now;
        ResumeAt = now + duration;
    }

    public void PauseUntilResume(DateTimeOffset now)
    {
        UntilResume = true;
        ReminderShown = false;
        PausedAt = now;
        ResumeAt = null;
    }

    public void Resume()
    {
        UntilResume = false;
        ReminderShown = false;
        PausedAt = null;
        ResumeAt = null;
    }

    public bool IsPaused(DateTimeOffset now)
    {
        if (UntilResume)
            return true;
        return ResumeAt is DateTimeOffset at && now < at;
    }

    public bool ShouldRemind(DateTimeOffset now)
    {
        if (!UntilResume || ReminderShown || PausedAt is null)
            return false;
        return now - PausedAt.Value >= TimeSpan.FromHours(8);
    }

    public void MarkReminded() => ReminderShown = true;

    public string Status(DateTimeOffset now)
    {
        if (!IsPaused(now))
            return TextCatalog.Get("protectionOn");
        if (UntilResume || ResumeAt is null)
            return TextCatalog.Get("pausedUntilResume");
        return TextCatalog.Format("pausedUntil", ("time", ResumeAt.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)));
    }
}
