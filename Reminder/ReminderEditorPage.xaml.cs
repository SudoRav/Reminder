using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Reminder;

public partial class ReminderEditorPage : ContentPage
{
    private enum DisplayBoundary
    {
        Start,
        End,
    }

    private readonly ReminderItem? reminder;
    private DateTime? displayStart;
    private DateTime? displayEnd;
    private readonly ObservableCollection<NotificationTimeItem> notificationTimes;
    private DisplayBoundary selectedBoundary;
    private bool isUpdatingPickers;
    private NotificationTimeItem? editingNotification;
    private bool isInitializing = true;
    private CancellationTokenSource? autoSaveCancellation;

    //автосохранение
    private bool isAutoSaveEnabled = false; // или true - по умолчанию

    public event EventHandler<ReminderItem>? SaveRequested;

    public event EventHandler? DeleteRequested;

    public ReminderEditorPage(ReminderItem? reminder = null)
    {
        InitializeComponent();

        this.reminder = reminder;
        ReminderTextEditor.Text = reminder?.Text ?? string.Empty;
        displayStart = reminder?.DisplayStart;
        displayEnd = reminder?.DisplayEnd;

        if (reminder is not null)
        {
            reminder.NormalizeNotificationSettings();
        }

        notificationTimes = new ObservableCollection<NotificationTimeItem>(
            reminder?.NotificationTimeSettings
                .OrderBy(x => x.Time)
                .Select(x => new NotificationTimeItem(x))
            ?? Enumerable.Empty<NotificationTimeItem>());

        foreach (NotificationTimeItem item in notificationTimes)
        {
            item.PropertyChanged += OnNotificationTimeItemChanged;
        }

        NotificationTimesCollectionView.ItemsSource = notificationTimes;

        DeleteButton.IsVisible = reminder is not null;

        StartRadioButton.IsChecked = false;
        EndRadioButton.IsChecked = true;

        UpdateDisplayPeriodLabel();
        isInitializing = false;

        //DisplayPeriodLabel.IsVisible = false;
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        DisplayPeriodLabel.IsVisible = true;

        selectedBoundary = DisplayBoundary.Start;
        ShowDateTimePicker(displayStart, TimeSpan.Zero);

        StartRadioButton.IsChecked = true;
        EndRadioButton.IsChecked = false;
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        DisplayPeriodLabel.IsVisible = true;

        selectedBoundary = DisplayBoundary.End;

        ShowDateTimePicker(
            displayEnd ?? DateTime.Today.AddDays(1).AddHours(23).AddMinutes(0),
            new TimeSpan(23, 0, 0));

        StartRadioButton.IsChecked = false;
        EndRadioButton.IsChecked = true;
    }

    private void ShowDateTimePicker(DateTime? dateTime, TimeSpan defaultTime)
    {
        DateTime initialDate;
        TimeSpan initialTime;

        if (dateTime.HasValue)
        {
            initialDate = dateTime.Value.Date;
            initialTime = dateTime.Value.TimeOfDay;
        }
        else if (selectedBoundary == DisplayBoundary.End)
        {
            initialDate = DateTime.Today.AddDays(1);
            initialTime = new TimeSpan(23, 0, 0);
        }
        else
        {
            initialDate = DateTime.Today;
            initialTime = defaultTime;
        }

        isUpdatingPickers = true;
        OverlayDatePicker.Date = initialDate;
        OverlayTimePicker.Time = initialTime;
        isUpdatingPickers = false;

        DateTimeOverlayTitle.Text = selectedBoundary == DisplayBoundary.Start
            ? "Выберите дату/время начала"
            : "Выберите дату/время конца";

        UpdateSelectedDateTimeLabels();
        DateTimePickerOverlay.IsVisible = true;
    }

    private void OnDateRowTapped(object? sender, TappedEventArgs e)
    {
        _ = OpenPickerAsync(OverlayDatePicker);
    }

    private void OnTimeRowTapped(object? sender, TappedEventArgs e)
    {
        _ = OpenPickerAsync(OverlayTimePicker);
    }

    private void OnDisplayDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (!isUpdatingPickers)
        {
            UpdateSelectedDateTimeLabels();
        }
    }

    private void OnDisplayTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!isUpdatingPickers && e.PropertyName == TimePicker.TimeProperty.PropertyName)
        {
            UpdateSelectedDateTimeLabels();
        }
    }

    private void OnCancelDateTimeClicked(object? sender, EventArgs e)
    {
        DateTimePickerOverlay.IsVisible = false;
    }

    private void OnSaveDateTimeClicked(object? sender, EventArgs e)
    {
        ApplySelectedDateTime();
        DateTimePickerOverlay.IsVisible = false;
    }

    private void UpdateSelectedDateTimeLabels()
    {
        SelectedDateLabel.Text = OverlayDatePicker.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        SelectedTimeLabel.Text = OverlayTimePicker.Time.ToString(@"hh\:mm", CultureInfo.CurrentCulture);
    }

    private static async Task OpenPickerAsync(View picker)
    {
        await Task.Delay(50);

        bool focused = await picker.Dispatcher.DispatchAsync(picker.Focus);
        if (!focused)
        {
            OpenPickerWithIsOpenProperty(picker);
        }
    }

    private static bool OpenPickerWithIsOpenProperty(View picker)
    {
        PropertyInfo? isOpenProperty = picker.GetType().GetProperty("IsOpen");
        if (isOpenProperty?.PropertyType != typeof(bool) || !isOpenProperty.CanWrite)
        {
            return false;
        }

        isOpenProperty.SetValue(picker, true);
        return true;
    }

    private void ApplySelectedDateTime()
    {
        DateTime value = OverlayDatePicker.Date + OverlayTimePicker.Time;

        // Редактирование времени уведомления
        if (editingNotification is not null)
        {
            editingNotification.Time = value;

            SortNotifications();

            NotificationTimesCollectionView.ItemsSource = null;
            NotificationTimesCollectionView.ItemsSource = notificationTimes;

            editingNotification = null;
            RequestAutoSave();
            return;
        }

        // Редактирование начала отображения
        if (selectedBoundary == DisplayBoundary.Start)
        {
            displayStart = value;
        }
        // Редактирование конца отображения
        else
        {
            displayEnd = value;
        }

        UpdateDisplayPeriodLabel();
        RequestAutoSave();
    }

    private void OnReminderChanged(object? sender, TextChangedEventArgs e)
    {
        RequestAutoSave();
    }

    private void RequestSave()
    {
        if (isInitializing)
        {
            return;
        }

        string text = ReminderTextEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        SaveRequested?.Invoke(this, new ReminderItem
        {
            Id = reminder?.Id ?? 0,
            Text = text,
            DisplayStart = displayStart,
            DisplayEnd = displayEnd,
            NotificationTimes = notificationTimes
                .Select(x => x.Time)
                .Order()
                .ToList(),
            NotificationTimeSettings = notificationTimes
                .OrderBy(x => x.Time)
                .Select(x => x.ToSettings())
                .ToList(),
        });
    }

    private void RequestAutoSave()
    {
        if (!isAutoSaveEnabled || isInitializing)
            return;

        autoSaveCancellation?.Cancel();
        autoSaveCancellation = new CancellationTokenSource();
        _ = RequestAutoSaveAsync(autoSaveCancellation.Token);
    }

    private async Task RequestAutoSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(700, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        string text = ReminderTextEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SaveRequested?.Invoke(this, new ReminderItem
            {
                Id = reminder?.Id ?? 0,
                Text = text,
                DisplayStart = displayStart,
                DisplayEnd = displayEnd,
                NotificationTimes = notificationTimes
                    .Select(x => x.Time)
                    .Order()
                    .ToList(),
                NotificationTimeSettings = notificationTimes
                    .OrderBy(x => x.Time)
                    .Select(x => x.ToSettings())
                    .ToList()
            });
        });
    }

    private bool isDeleting;
    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (reminder is null)
            return;

        isDeleting = true;

        DeleteRequested?.Invoke(this, EventArgs.Empty);

        await Navigation.PopModalAsync();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (isInitializing)
            return;

        string text = ReminderTextEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlert(
                "Ошибка",
                "Введите текст напоминания.",
                "OK");

            return;
        }

        ReminderItem savedReminder = new()
        {
            Id = reminder?.Id ?? 0,
            Text = text,
            DisplayStart = displayStart,
            DisplayEnd = displayEnd,
            NotificationTimes = notificationTimes
                .Select(x => x.Time)
                .Order()
                .ToList(),
            NotificationTimeSettings = notificationTimes
                .OrderBy(x => x.Time)
                .Select(x => x.ToSettings())
                .ToList()
        };

        SaveRequested?.Invoke(this, savedReminder);

        isDeleting = true;

        await Navigation.PopModalAsync();
    }

    private async void OnAddWeekNotificationClicked(object? sender, EventArgs e)
    {
        await AddNotificationTimeAsync(TimeSpan.FromDays(7));
    }

    private async void OnAddDayNotificationClicked(object? sender, EventArgs e)
    {
        await AddNotificationTimeAsync(TimeSpan.FromDays(1));
    }

    private async void OnAddHourNotificationClicked(object? sender, EventArgs e)
    {
        await AddNotificationTimeAsync(TimeSpan.FromHours(1));
    }

    private async Task AddNotificationTimeAsync(TimeSpan offset)
    {
        DateTime? targetDateTime = StartRadioButton.IsChecked
            ? displayStart
            : displayEnd;

        if (targetDateTime is null)
        {
            string targetName = StartRadioButton.IsChecked ? "начала" : "конца";
            await DisplayAlert("Ошибка", $"Сначала выберите дату/время {targetName}.", "OK");
            return;
        }

        AddNotificationTime(targetDateTime.Value - offset);
    }

    private void OnNotificationTargetChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value || sender is not RadioButton radioButton)
        {
            return;
        }

        selectedBoundary = radioButton == EndRadioButton
            ? DisplayBoundary.End
            : DisplayBoundary.Start;
    }

    private void AddNotificationTime(DateTime notificationTime)
    {
        if (!notificationTimes.Any(x => x.Time == notificationTime))
        {
            NotificationTimeItem item = new(notificationTime);
            item.PropertyChanged += OnNotificationTimeItemChanged;
            notificationTimes.Add(item);
            SortNotifications();
            RequestAutoSave();
        }
    }

    private void OnDeleteNotificationClicked(object? sender, EventArgs e)
    {
        if (sender is Button button &&
            button.CommandParameter is NotificationTimeItem item)
        {
            item.PropertyChanged -= OnNotificationTimeItemChanged;
            notificationTimes.Remove(item);
            RequestAutoSave();
        }
    }

    private void OnNotificationTimeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Label label &&
            label.BindingContext is NotificationTimeItem item)
        {
            editingNotification = item;

            ShowDateTimePicker(item.Time, item.Time.TimeOfDay);
        }
    }

    private void OnNotificationTimeItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        RequestAutoSave();
    }

    private void SortNotifications()
    {
        List<NotificationTimeItem> sorted =
            notificationTimes.OrderBy(x => x.Time).ToList();

        notificationTimes.Clear();

        foreach (NotificationTimeItem item in sorted)
        {
            notificationTimes.Add(item);
        }
    }

    private void UpdateDisplayPeriodLabel()
    {
        DisplayPeriodLabel.Text = ReminderDisplayFormatter.GetDisplayText(displayStart, displayEnd);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        autoSaveCancellation?.Cancel();

        if (!isDeleting)
        {
            RequestSave();
        }
    }

    private async void settime900(object? sender, EventArgs e)
    {
        OverlayTimePicker.Time = new TimeSpan(9, 0, 0);
    }

    private async void settime2100(object? sender, EventArgs e)
    {
        OverlayTimePicker.Time = new TimeSpan(21, 0, 0);
    }

    private async void settime1500(object? sender, EventArgs e)
    {
        OverlayTimePicker.Time = new TimeSpan(15, 0, 0);
    }

    private async void settime300(object? sender, EventArgs e)
    {
        OverlayTimePicker.Time = new TimeSpan(3, 0, 0);
    }
}
