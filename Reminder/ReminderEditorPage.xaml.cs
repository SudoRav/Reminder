namespace Reminder;

public partial class ReminderEditorPage : ContentPage
{
    private readonly ReminderItem? reminder;
    private readonly Editor reminderTextEditor;
    private readonly Button deleteButton;
    private readonly Label displayPeriodLabel;
    private readonly Grid dateTimePickerPanel;
    private readonly Label dateTimePickerTitle;
    private readonly DatePicker displayDatePicker;
    private readonly TimePicker displayTimePicker;
    private DateTime? displayStart;
    private DateTime? displayEnd;

    public event EventHandler<ReminderItem>? SaveRequested;

    public event EventHandler? DeleteRequested;

    public ReminderEditorPage(ReminderItem? reminder = null)
    {
        InitializeComponent();

        reminderTextEditor = GetRequiredView<Editor>(nameof(ReminderTextEditor));
        deleteButton = GetRequiredView<Button>(nameof(DeleteButton));
        displayPeriodLabel = GetRequiredView<Label>(nameof(DisplayPeriodLabel));
        dateTimePickerPanel = GetRequiredView<Grid>(nameof(DateTimePickerPanel));
        dateTimePickerTitle = GetRequiredView<Label>(nameof(DateTimePickerTitle));
        displayDatePicker = GetRequiredView<DatePicker>(nameof(DisplayDatePicker));
        displayTimePicker = GetRequiredView<TimePicker>(nameof(DisplayTimePicker));

        this.reminder = reminder;
        reminderTextEditor.Text = reminder?.Text ?? string.Empty;
        displayStart = reminder?.DisplayStart;
        displayEnd = reminder?.DisplayEnd;
        deleteButton.IsVisible = reminder is not null;
        UpdateDisplayPeriodLabel();
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        DateTime value = displayStart ?? DateTime.Today;
        displayDatePicker.Date = value.Date;
        displayTimePicker.Time = displayStart?.TimeOfDay ?? TimeSpan.Zero;
        dateTimePickerTitle.Text = "Начало показа";
        dateTimePickerPanel.IsVisible = true;
        DetachDateTimePickerHandlers();
        displayDatePicker.Unfocused += OnStartDateTimePicked;
        displayTimePicker.Unfocused += OnStartDateTimePicked;
        displayDatePicker.Focus();
    }

    private void OnEndClicked(object? sender, EventArgs e)
    {
        DateTime value = displayEnd ?? DateTime.Today;
        displayDatePicker.Date = value.Date;
        displayTimePicker.Time = displayEnd?.TimeOfDay ?? new TimeSpan(23, 59, 0);
        dateTimePickerTitle.Text = "Конец показа";
        dateTimePickerPanel.IsVisible = true;
        DetachDateTimePickerHandlers();
        displayDatePicker.Unfocused += OnEndDateTimePicked;
        displayTimePicker.Unfocused += OnEndDateTimePicked;
        displayDatePicker.Focus();
    }

    private void OnStartDateTimePicked(object? sender, FocusEventArgs e)
    {
        displayStart = displayDatePicker.Date + displayTimePicker.Time;
        UpdateDisplayPeriodLabel();
    }

    private void OnEndDateTimePicked(object? sender, FocusEventArgs e)
    {
        displayEnd = displayDatePicker.Date + displayTimePicker.Time;
        UpdateDisplayPeriodLabel();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        string text = reminderTextEditor.Text?.Trim() ?? string.Empty;
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
        displayDatePicker.Unfocused -= OnStartDateTimePicked;
        displayTimePicker.Unfocused -= OnStartDateTimePicked;
        displayDatePicker.Unfocused -= OnEndDateTimePicked;
        displayTimePicker.Unfocused -= OnEndDateTimePicked;
    }

    private TView GetRequiredView<TView>(string name)
        where TView : Element
    {
        return this.FindByName<TView>(name)
            ?? throw new InvalidOperationException($"XAML element '{name}' was not found.");
    }

    private void UpdateDisplayPeriodLabel()
    {
        displayPeriodLabel.Text = ReminderDisplayFormatter.GetEditorDisplayText(displayStart, displayEnd);
    }
}
