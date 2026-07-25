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
        ShowDateTimePicker(displayStart, TimeSpan.Zero);
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        selectedBoundary = DisplayBoundary.End;

        ShowDateTimePicker(
            displayEnd ?? DateTime.Today.AddDays(1).AddHours(23).AddMinutes(59),
            new TimeSpan(23, 59, 0));
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
            initialTime = new TimeSpan(23, 59, 0);
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
            ? "Select start date & time"
            : "Select end date & time";

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
