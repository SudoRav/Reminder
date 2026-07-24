namespace Reminder;

public interface IReminderNotificationService
{
    Task ShowAsync(ReminderItem reminder);

    void Cancel(int reminderId);
}
