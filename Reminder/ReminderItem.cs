using Reminder;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Reminder;

public sealed class ReminderItem : INotifyPropertyChanged
{
    private DateTime? displayStart;
    private DateTime? displayEnd;
    private int group = 3;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime? DisplayStart
    {
        get => displayStart;
        set => SetField(ref displayStart, value);
    }

    public DateTime? DisplayEnd
    {
        get => displayEnd;
        set => SetField(ref displayEnd, value);
    }

    /// <summary>
    /// Текущая группа напоминания:
    /// 1 - красная
    /// 2 - жёлтая
    /// 3 - прозрачная
    /// 4 - синяя
    /// </summary>
    public int Group
    {
        get => group;
        set => SetField(ref group, Math.Clamp(value, 1, 4));
    }

    public bool AutoCompleteOnDisplayEnd { get; set; }

    /// <summary>
    /// Controls whether a reminder without a display period is kept in the
    /// device notification center. Timed reminders always enable this value.
    /// </summary>
    public bool ShowInNotificationCenter { get; set; } = true;

    public List<DateTime> NotificationTimes { get; set; } = [];

    public List<NotificationTimeSettings> NotificationTimeSettings { get; set; } = [];

    public DateTime? CompletedAt { get; set; }

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

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}