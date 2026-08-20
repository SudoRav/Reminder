namespace Reminder;

public sealed class ReminderItem
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime? DisplayStart { get; set; }

    public DateTime? DisplayEnd { get; set; }

    public List<DateTime> NotificationTimes { get; set; } = []; 

    public List<NotificationTimeSettings> NotificationTimeSettings { get; set; } = [];

    public NotificationTimeSettings GetNotificationSettings(DateTime notificationTime)
    {
        NotificationTimeSettings? settings = NotificationTimeSettings
            .FirstOrDefault(item => item.Time == notificationTime);

        if (settings is not null)
        {
            return settings;
        }

        return new NotificationTimeSettings
        {
            Time = notificationTime,
            IsPushEnabled = true,
            IsOverlayEnabled = true,
            IsAlarmEnabled = false,
        };
    }

    public void NormalizeNotificationSettings()
    {
        NotificationTimeSettings = NotificationTimes
            .Distinct()
            .Order()
            .Select(GetNotificationSettings)
            .ToList();
    }
}
