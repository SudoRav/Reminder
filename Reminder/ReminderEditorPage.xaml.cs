using System.ComponentModel;

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
    private DisplayBoundary selectedBoundary;
    private bool isUpdatingPickers;

    public event EventHandler<ReminderItem>? SaveRequested;

    public event EventHandler? DeleteRequested;

    public ReminderEditorPage(ReminderItem? reminder = null)
    {
        InitializeComponent();

        this.reminder = reminder;
        ReminderTextEditor.Text = reminder?.Text ?? string.Empty;
        displayStart = reminder?.DisplayStart;
        displayEnd = reminder?.DisplayEnd;
        DeleteButton.IsVisible = reminder is not null;
        UpdateDisplayPeriodLabel();
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        selectedBoundary = DisplayBoundary.Start;
        _ = ShowDateTimePickerAsync(displayStart ?? DateTime.Today, TimeSpan.Zero, "Выберите дату начала");
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        selectedBoundary = DisplayBoundary.End;
        _ = ShowDateTimePickerAsync(displayEnd ?? DateTime.Today, new TimeSpan(23, 59, 0), "Выберите дату окончания");
    }

    private async Task ShowDateTimePickerAsync(DateTime dateTime, TimeSpan defaultTime, string title)
    {
        isUpdatingPickers = true;
        DisplayDatePicker.Date = dateTime.Date;
        DisplayTimePicker.Time = dateTime.TimeOfDay == TimeSpan.Zero && selectedBoundary == DisplayBoundary.End
            ? defaultTime
            : dateTime.TimeOfDay;
        DateTimePickerTitle.Text = title;
        ShowCalendarPicker();
        DateTimePickerPanel.IsVisible = true;
        isUpdatingPickers = false;
        ApplySelectedDateTime();

        await Task.Delay(150);
        DisplayDatePicker.Focus();
    }

    private void OnDisplayDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (!isUpdatingPickers)
        {
            ApplySelectedDateTime();
        }
    }

    private void OnDisplayTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!isUpdatingPickers && e.PropertyName == TimePicker.TimeProperty.PropertyName)
        {
            ApplySelectedDateTime();
        }
    }

    private void OnDateTimePickerSwiped(object? sender, SwipedEventArgs e)
    {
        if (DisplayDatePicker.IsVisible)
        {
            ShowClockPicker();
            DisplayTimePicker.Focus();
            return;
        }

        ShowCalendarPicker();
        DisplayDatePicker.Focus();
    }

    private void ApplySelectedDateTime()
    {
        DateTime value = DisplayDatePicker.Date + DisplayTimePicker.Time;

        if (selectedBoundary == DisplayBoundary.Start)
        {
            displayStart = value;
        }
        else
        {
            displayEnd = value;
        }

        UpdateDisplayPeriodLabel();
    }

    private void ShowCalendarPicker()
    {
        DisplayDatePicker.IsVisible = true;
        DisplayTimePicker.IsVisible = false;
        CalendarModeIndicator.BackgroundColor = Color.FromArgb("#7C4DFF");
        ClockModeIndicator.BackgroundColor = Colors.Transparent;
        DateTimePickerHint.Text = "Свайпните влево или вправо, чтобы перейти к выбору времени";
    }

    private void ShowClockPicker()
    {
        DisplayDatePicker.IsVisible = false;
        DisplayTimePicker.IsVisible = true;
        CalendarModeIndicator.BackgroundColor = Colors.Transparent;
        ClockModeIndicator.BackgroundColor = Color.FromArgb("#7C4DFF");
        DateTimePickerHint.Text = "Свайпните влево или вправо, чтобы вернуться к календарю";
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        string text = ReminderTextEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlert("Ошибка", "Введите текст напоминания.", "OK");
            return;
        }

        SaveRequested?.Invoke(this, new ReminderItem
        {
            Id = reminder?.Id ?? 0,
            Text = text,
            DisplayStart = displayStart,
            DisplayEnd = displayEnd,
        });
        await Navigation.PopModalAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (reminder is null)
        {
            return;
        }

        DeleteRequested?.Invoke(this, EventArgs.Empty);
        await Navigation.PopModalAsync();
    }

    private void UpdateDisplayPeriodLabel()
    {
        DisplayPeriodLabel.Text = ReminderDisplayFormatter.GetEditorDisplayText(displayStart, displayEnd);
    }
}
