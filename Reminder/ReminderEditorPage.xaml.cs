using System.ComponentModel;
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
    private DisplayBoundary selectedBoundary;
    private bool isUpdatingPickers;
    private bool isWaitingForDatePicker;
    private bool isWaitingForTimePicker;
    private bool hasDatePickerOpened;
    private bool hasTimePickerOpened;

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
        _ = ShowDateTimePickerAsync(displayStart ?? DateTime.Today, TimeSpan.Zero);
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        selectedBoundary = DisplayBoundary.End;
        _ = ShowDateTimePickerAsync(displayEnd ?? DateTime.Today, new TimeSpan(23, 59, 0));
    }

    private async Task ShowDateTimePickerAsync(DateTime dateTime, TimeSpan defaultTime)
    {
        isUpdatingPickers = true;
        isWaitingForDatePicker = true;
        isWaitingForTimePicker = false;
        hasDatePickerOpened = false;
        hasTimePickerOpened = false;
        StartButton.IsEnabled = false;
        EndButton.IsEnabled = false;
        DisplayDatePicker.Date = dateTime.Date;
        DisplayTimePicker.Time = dateTime.TimeOfDay == TimeSpan.Zero && selectedBoundary == DisplayBoundary.End
            ? defaultTime
            : dateTime.TimeOfDay;
        isUpdatingPickers = false;

        hasDatePickerOpened = await FocusPickerAsync(DisplayDatePicker);
        if (!hasDatePickerOpened)
        {
            CompleteDateTimeSelection();
        }
    }

    private void OnDisplayDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (!isUpdatingPickers && isWaitingForDatePicker)
        {
            _ = ShowTimePickerAsync();
        }
    }

    private void OnDisplayDatePickerUnfocused(object? sender, FocusEventArgs e)
    {
        if (!isUpdatingPickers && isWaitingForDatePicker && hasDatePickerOpened)
        {
            _ = ShowTimePickerAsync();
        }
    }

    private async Task ShowTimePickerAsync()
    {
        if (!isWaitingForDatePicker || isWaitingForTimePicker)
        {
            return;
        }

        isWaitingForDatePicker = false;
        isWaitingForTimePicker = true;
        hasTimePickerOpened = await FocusPickerAsync(DisplayTimePicker);
        if (!hasTimePickerOpened)
        {
            CompleteDateTimeSelection();
        }
    }

    private void OnDisplayTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!isUpdatingPickers && e.PropertyName == TimePicker.TimeProperty.PropertyName)
        {
            CompleteDateTimeSelection();
        }
    }

    private void OnDisplayTimePickerUnfocused(object? sender, FocusEventArgs e)
    {
        if (!isUpdatingPickers && isWaitingForTimePicker && hasTimePickerOpened)
        {
            CompleteDateTimeSelection();
        }
    }

    private void CompleteDateTimeSelection()
    {
        isWaitingForDatePicker = false;
        isWaitingForTimePicker = false;
        hasDatePickerOpened = false;
        hasTimePickerOpened = false;
        StartButton.IsEnabled = true;
        EndButton.IsEnabled = true;
        ApplySelectedDateTime();
    }

    private static async Task<bool> FocusPickerAsync(View picker)
    {
        await Task.Delay(150);

        bool focused = await picker.Dispatcher.DispatchAsync(picker.Focus);
        if (focused)
        {
            return true;
        }

        if (OpenPickerWithIsOpenProperty(picker))
        {
            return true;
        }

        return false;
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
