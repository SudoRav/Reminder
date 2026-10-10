
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Reminder;

public class NotificationTimeItem : INotifyPropertyChanged
{
    private DateTime time;
    private bool isPushEnabled;
    private bool isOverlayEnabled;
    private bool isAlarmEnabled;

    // Длительность таймера, заданная кнопкой 🔁.
    // null означает, что длительность ещё не задана.
    private TimeSpan? timerDuration;

public bool HasTimerDuration => timerDuration.HasValue;

    public string TimerDurationText
    {
        get
        {
            if (!timerDuration.HasValue)
                return string.Empty;

            TimeSpan duration = timerDuration.Value;

            return $"Периодичность: {duration.Days:D2}:{duration.Hours:D2}:{duration.Minutes:D2}";
        }
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    // ============================================================
    // ДАТА И ВРЕМЯ ОПОВЕЩЕНИЯ
    // ============================================================

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

    // ============================================================
    // ДЛИТЕЛЬНОСТЬ ТАЙМЕРА
    // ============================================================

    public TimeSpan? TimerDuration
    {
        get => timerDuration;
        set
        {
            if (SetProperty(ref timerDuration, value))
            {
                // Производные свойства, на которые подписан интерфейс.
                OnPropertyChanged(nameof(HasTimerDuration));
                OnPropertyChanged(nameof(TimerDurationText));
            }
        }
    }

    // ============================================================
    // НАСТРОЙКИ ОПОВЕЩЕНИЯ
    // ============================================================

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

    // ============================================================
    // ОТОБРАЖЕНИЕ
    // ============================================================

    public string DisplayText =>
        ReminderDisplayFormatter.FormatNotificationTime(Time);

    // ============================================================
    // СОЗДАНИЕ НОВОГО ОПОВЕЩЕНИЯ
    // ============================================================

    // Настройки по умолчанию:
    // Overlay = включён
    // Push    = выключен
    // Alarm   = выключен
    // TimerDuration = не задан

    public NotificationTimeItem(DateTime time)
        : this(new NotificationTimeSettings
        {
            Time = time,
            IsOverlayEnabled = true,
            IsPushEnabled = false,
            IsAlarmEnabled = false,
            TimerDuration = null
        })
    {
    }

    // ============================================================
    // СОЗДАНИЕ ИЗ СОХРАНЁННЫХ НАСТРОЕК
    // ============================================================

    // Все настройки восстанавливаются без изменения значений.

    public NotificationTimeItem(NotificationTimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        time = settings.Time;
        isPushEnabled = settings.IsPushEnabled;
        isOverlayEnabled = settings.IsOverlayEnabled;
        isAlarmEnabled = settings.IsAlarmEnabled;
        timerDuration = settings.TimerDuration;
    }

    // ============================================================
    // ПРЕОБРАЗОВАНИЕ В НАСТРОЙКИ ДЛЯ СОХРАНЕНИЯ
    // ============================================================

    public NotificationTimeSettings ToSettings() => new()
    {
        Time = Time,
        IsPushEnabled = IsPushEnabled,
        IsOverlayEnabled = IsOverlayEnabled,
        IsAlarmEnabled = IsAlarmEnabled,
        TimerDuration = TimerDuration
    };

    // ============================================================
    // УВЕДОМЛЕНИЕ ОБ ИЗМЕНЕНИИ СВОЙСТВ
    // ============================================================

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;

        OnPropertyChanged(propertyName);

        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
