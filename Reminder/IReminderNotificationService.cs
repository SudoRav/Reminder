namespace Reminder;

public interface IReminderNotificationService
{
    Task ShowAsync(ReminderItem reminder);

    Task ScheduleAsync(ReminderItem reminder);

    void Cancel(int reminderId);
}
