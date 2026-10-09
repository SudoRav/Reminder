namespace Reminder;

public interface IReminderNotificationService
{
    /// <summary>
    /// Будильники этого напоминания + пересборка списка в шторке
    /// (вызывать после сохранения в хранилище).
    /// </summary>
    Task ShowAsync(ReminderItem reminder);

    /// <summary>
    /// Только будильники и разрешения. Шторку не трогает.
    /// </summary>
    Task ScheduleAsync(ReminderItem reminder);

    void Cancel(int reminderId);

    /// <summary>
    /// Пересобирает список в notification center в переданном порядке.
    /// Реализация по умолчанию ничего не делает (для платформ-заглушек).
    /// </summary>
    void SyncNotificationCenter(
        IReadOnlyList<ReminderItem> orderedReminders)
    {
    }
}
