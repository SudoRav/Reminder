namespace Reminder;

public sealed class NotificationTimeSettings
{
    public DateTime Time { get; set; }

    public bool IsPushEnabled { get; set; } = true;

    public bool IsOverlayEnabled { get; set; } = true;

    public bool IsAlarmEnabled { get; set; }
}
