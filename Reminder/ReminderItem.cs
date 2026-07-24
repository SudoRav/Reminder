namespace Reminder;

public sealed class ReminderItem
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime? DisplayStart { get; set; }

    public DateTime? DisplayEnd { get; set; }
}
