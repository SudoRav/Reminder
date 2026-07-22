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
        ShowDateTimePicker(displayStart ?? DateTime.Today, TimeSpan.Zero, "Начало показа");
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        selectedBoundary = DisplayBoundary.End;
        ShowDateTimePicker(displayEnd ?? DateTime.Today, new TimeSpan(23, 59, 0), "Конец показа");
    }

    private void ShowDateTimePicker(DateTime dateTime, TimeSpan defaultTime, string title)
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
        if (e.Direction == SwipeDirection.Right)
        {
            ShowClockPicker();
            return;
        }

        if (e.Direction == SwipeDirection.Left)
        {
            ShowCalendarPicker();
        }
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
    }

    private void ShowClockPicker()
    {
        DisplayDatePicker.IsVisible = false;
        DisplayTimePicker.IsVisible = true;
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
