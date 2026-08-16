using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Reminder;

public class NotificationTimeItem : INotifyPropertyChanged
{
    private DateTime time;
    private bool isPushEnabled;
    private bool isOverlayEnabled;
    private bool isAlarmEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime Time
    {
        get => time;
        set
        {
            if (time == value)
            {
                return;
            }

            time = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public bool IsPushEnabled
    {
        get => isPushEnabled;
        set => SetProperty(ref isPushEnabled, value);
    }

    public bool IsOverlayEnabled
    {
        get => isOverlayEnabled;
        set => SetProperty(ref isOverlayEnabled, value);
    }

    public bool IsAlarmEnabled
    {
        get => isAlarmEnabled;
        set => SetProperty(ref isAlarmEnabled, value);
    }

    public string DisplayText =>
        ReminderDisplayFormatter.FormatNotificationTime(Time);

    public NotificationTimeItem(DateTime time)
        : this(new NotificationTimeSettings { Time = time })
    {
    }

    public NotificationTimeItem(NotificationTimeSettings settings)
    {
        time = settings.Time;
        isPushEnabled = settings.IsPushEnabled;
        isOverlayEnabled = settings.IsOverlayEnabled;
        isAlarmEnabled = settings.IsAlarmEnabled;
    }

    public NotificationTimeSettings ToSettings() => new()
    {
        Time = Time,
        IsPushEnabled = IsPushEnabled,
        IsOverlayEnabled = IsOverlayEnabled,
        IsAlarmEnabled = IsAlarmEnabled,
    };

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
