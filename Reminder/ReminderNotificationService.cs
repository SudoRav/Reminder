namespace Reminder;

public sealed class ReminderNotificationService : IReminderNotificationService
{
    public Task ShowAsync(ReminderItem reminder)
    {
        return Task.CompletedTask;
    }

    public Task ScheduleAsync(ReminderItem reminder)
    {
        return Task.CompletedTask;
    }

    public void Cancel(int reminderId)
    {
    }
}
