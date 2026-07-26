namespace Reminder;

public class NotificationTimeItem
{
    public DateTime Time { get; set; }

    public string DisplayText =>
        ReminderDisplayFormatter.FormatNotificationTime(Time);

    public NotificationTimeItem(DateTime time)
    {
        Time = time;
    }
}