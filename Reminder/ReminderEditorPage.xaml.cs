namespace Reminder;

public partial class ReminderEditorPage : ContentPage
{
    private readonly ReminderItem? reminder;
    private DateTime? displayStart;
    private DateTime? displayEnd;

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
        DateTime value = displayStart ?? DateTime.Today;
        DisplayDatePicker.Date = value.Date;
        DisplayTimePicker.Time = displayStart?.TimeOfDay ?? TimeSpan.Zero;
        DateTimePickerTitle.Text = "Начало показа";
        DateTimePickerPanel.IsVisible = true;
        DetachDateTimePickerHandlers();
        DisplayDatePicker.Unfocused += OnStartDateTimePicked;
        DisplayTimePicker.Unfocused += OnStartDateTimePicked;
        DisplayDatePicker.Focus();
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        DateTime value = displayEnd ?? DateTime.Today;
        DisplayDatePicker.Date = value.Date;
        DisplayTimePicker.Time = displayEnd?.TimeOfDay ?? new TimeSpan(23, 59, 0);
        DateTimePickerTitle.Text = "Конец показа";
        DateTimePickerPanel.IsVisible = true;
        DetachDateTimePickerHandlers();
        DisplayDatePicker.Unfocused += OnEndDateTimePicked;
        DisplayTimePicker.Unfocused += OnEndDateTimePicked;
        DisplayDatePicker.Focus();
    }

    private void OnStartDateTimePicked(object? sender, FocusEventArgs e)
    {
        displayStart = DisplayDatePicker.Date + DisplayTimePicker.Time;
        UpdateDisplayPeriodLabel();
    }

    private void OnEndDateTimePicked(object? sender, FocusEventArgs e)
    {
        displayEnd = DisplayDatePicker.Date + DisplayTimePicker.Time;
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

        bool confirmed = await DisplayAlert(
            "Завершить напоминание?",
            "Напоминание исчезнет из списка и из уведомлений.",
            "Завершить",
            "Отмена");

        if (!confirmed)
        {
            return;
        }

        DeleteRequested?.Invoke(this, EventArgs.Empty);
        await Navigation.PopModalAsync();
    }

    private void DetachDateTimePickerHandlers()
    {
        DisplayDatePicker.Unfocused -= OnStartDateTimePicked;
        DisplayTimePicker.Unfocused -= OnStartDateTimePicked;
        DisplayDatePicker.Unfocused -= OnEndDateTimePicked;
        DisplayTimePicker.Unfocused -= OnEndDateTimePicked;
    }

    private void UpdateDisplayPeriodLabel()
    {
        DisplayPeriodLabel.Text = ReminderDisplayFormatter.GetEditorDisplayText(displayStart, displayEnd);
    }
}
