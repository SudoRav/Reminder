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
    private bool autoCompleteOnDisplayEnd;
    private NotificationTimeItem? editingNotification;
    private bool isInitializing = true;
    private CancellationTokenSource? autoSaveCancellation;
    private readonly IDispatcherTimer countdownTimer;

    // Автосохранение
    private bool isAutoSaveEnabled = false;

    // ============================================================
    // ТАЙМЕР
    // ============================================================

    private const double TimerWheelItemHeight = 60;
    private const double TimerWheelTopPadding = 90;

    private int timerDays;
    private int timerHours;
    private int timerMinutes;

    private int pendingTimerDays;
    private int pendingTimerHours;
    private int pendingTimerMinutes;

    private CancellationTokenSource? daysSnapCancellation;
    private CancellationTokenSource? hoursSnapCancellation;
    private CancellationTokenSource? minutesSnapCancellation;

    // Не даёт программному ScrollToAsync снова запускать snap.
    private bool isTimerWheelProgrammaticScroll;

    public event EventHandler<ReminderItem>? SaveRequested;

    public event EventHandler? DeleteRequested;


    public ReminderEditorPage(ReminderItem? reminder = null)
    {
        InitializeComponent();

        countdownTimer = Dispatcher.CreateTimer();
        countdownTimer.Interval = TimeSpan.FromMinutes(1);
        countdownTimer.Tick += OnCountdownTimerTick;

        InitializeTimerWheels();

        this.reminder = reminder;

        ReminderTextEditor.Text = reminder?.Text ?? string.Empty;

        displayStart = reminder?.DisplayStart;
        displayEnd = reminder?.DisplayEnd;

        autoCompleteOnDisplayEnd =
            reminder?.AutoCompleteOnDisplayEnd ?? false;

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
        UpdateAutoCompleteControls();
        UpdateTimerFromDisplayEnd();

        isInitializing = false;
    }


    // ============================================================
    // ДАТА / ВРЕМЯ
    // ============================================================

    private void OnStartClicked(object? sender, EventArgs e)
    {
        DisplayPeriodLabel.IsVisible = true;

        selectedBoundary = DisplayBoundary.Start;

        ShowDateTimePicker(
            displayStart,
            TimeSpan.Zero);

        StartRadioButton.IsChecked = true;
        EndRadioButton.IsChecked = false;
    }


    private void OnEndClicked(object? sender, EventArgs e)
    {
        DisplayPeriodLabel.IsVisible = true;

        selectedBoundary = DisplayBoundary.End;

        DateTime defaultEnd =
            displayStart?.Date.AddDays(1)
            ?? DateTime.Today.AddDays(1);

        ShowDateTimePicker(
            displayEnd ?? defaultEnd + new TimeSpan(23, 0, 0),
            new TimeSpan(23, 0, 0));

        StartRadioButton.IsChecked = false;
        EndRadioButton.IsChecked = true;
    }


    private void ShowDateTimePicker(
        DateTime? dateTime,
        TimeSpan defaultTime)
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

        UpdateSelectedDateTimeLabels();
        UpdateTimerFromDateTimePicker();

        DateTimePickerOverlay.IsVisible = true;
    }


    private void OnDateRowTapped(
        object? sender,
        TappedEventArgs e)
    {
        _ = OpenPickerAsync(OverlayDatePicker);
    }


    private void OnTimeRowTapped(
        object? sender,
        TappedEventArgs e)
    {
        _ = OpenPickerAsync(OverlayTimePicker);
    }


    private void OnDisplayDateSelected(
        object? sender,
        DateChangedEventArgs e)
    {
        if (!isUpdatingPickers)
        {
            UpdateSelectedDateTimeLabels();
            UpdateTimerFromDateTimePicker();
        }
    }


    private void OnDisplayTimeChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!isUpdatingPickers &&
            e.PropertyName == TimePicker.TimeProperty.PropertyName)
        {
            UpdateSelectedDateTimeLabels();
            UpdateTimerFromDateTimePicker();
        }
    }


    private void OnCancelDateTimeClicked(
        object? sender,
        EventArgs e)
    {
        editingNotification = null;
        UpdateTimerFromDisplayEnd();
        DateTimePickerOverlay.IsVisible = false;
    }


    private void OnSaveDateTimeClicked(
        object? sender,
        EventArgs e)
    {
        ApplySelectedDateTime();

        DateTimePickerOverlay.IsVisible = false;
    }


    private void UpdateSelectedDateTimeLabels()
    {
        SelectedDateLabel.Text =
            OverlayDatePicker.Date.ToString(
                "d MMM yyyy",
                CultureInfo.CurrentCulture);

        SelectedTimeLabel.Text =
            OverlayTimePicker.Time.ToString(
                @"hh\:mm",
                CultureInfo.CurrentCulture);
    }


    private static async Task OpenPickerAsync(View picker)
    {
        await Task.Delay(50);

        bool focused =
            await picker.Dispatcher.DispatchAsync(picker.Focus);

        if (!focused)
        {
            OpenPickerWithIsOpenProperty(picker);
        }
    }


    private static bool OpenPickerWithIsOpenProperty(View picker)
    {
        PropertyInfo? isOpenProperty =
            picker.GetType().GetProperty("IsOpen");

        if (isOpenProperty?.PropertyType != typeof(bool) ||
            !isOpenProperty.CanWrite)
        {
            return false;
        }

        isOpenProperty.SetValue(picker, true);

        return true;
    }


    private void ApplySelectedDateTime()
    {
        DateTime value =
            OverlayDatePicker.Date +
            OverlayTimePicker.Time;

        // Редактирование времени уведомления
        if (editingNotification is not null)
        {
            editingNotification.Time = value;

            SortNotifications();

            NotificationTimesCollectionView.ItemsSource = null;
            NotificationTimesCollectionView.ItemsSource =
                notificationTimes;

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
            bool hadDisplayEnd = displayEnd is not null;

            displayEnd = value;

            if (!hadDisplayEnd)
            {
                autoCompleteOnDisplayEnd = false;
            }
        }

        UpdateDisplayPeriodLabel();
        UpdateAutoCompleteControls();
        UpdateTimerFromDisplayEnd();

        RequestAutoSave();
    }


    // ============================================================
    // АВТОСОХРАНЕНИЕ / СОХРАНЕНИЕ
    // ============================================================

    private void OnReminderChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        RequestAutoSave();
    }


    private void RequestSave()
    {
        if (isInitializing)
        {
            return;
        }

        string text =
            ReminderTextEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        SaveRequested?.Invoke(
            this,
            new ReminderItem
            {
                Id = reminder?.Id ?? 0,

                Text = text,

                DisplayStart = displayStart,

                DisplayEnd = displayEnd,

                AutoCompleteOnDisplayEnd =
                    autoCompleteOnDisplayEnd,

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
        if (!isAutoSaveEnabled ||
            isInitializing)
        {
            return;
        }

        autoSaveCancellation?.Cancel();

        autoSaveCancellation =
            new CancellationTokenSource();

        _ = RequestAutoSaveAsync(
            autoSaveCancellation.Token);
    }


    private async Task RequestAutoSaveAsync(
        CancellationToken token)
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
        {
            return;
        }

        string text =
            ReminderTextEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SaveRequested?.Invoke(
                this,
                new ReminderItem
                {
                    Id = reminder?.Id ?? 0,

                    Text = text,

                    DisplayStart = displayStart,

                    DisplayEnd = displayEnd,

                    AutoCompleteOnDisplayEnd =
                        autoCompleteOnDisplayEnd,

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


    private async void OnDeleteClicked(
        object? sender,
        EventArgs e)
    {
        if (reminder is null)
        {
            return;
        }

        isDeleting = true;

        DeleteRequested?.Invoke(
            this,
            EventArgs.Empty);

        await Navigation.PopModalAsync();
    }


    private async void OnSaveClicked(
        object? sender,
        EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        string text =
            ReminderTextEditor.Text?.Trim() ?? string.Empty;

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

            AutoCompleteOnDisplayEnd =
                autoCompleteOnDisplayEnd,

            NotificationTimes = notificationTimes
                .Select(x => x.Time)
                .Order()
                .ToList(),

            NotificationTimeSettings = notificationTimes
                .OrderBy(x => x.Time)
                .Select(x => x.ToSettings())
                .ToList()
        };

        SaveRequested?.Invoke(
            this,
            savedReminder);

        isDeleting = true;

        await Navigation.PopModalAsync();
    }


    // ============================================================
    // УВЕДОМЛЕНИЯ
    // ============================================================

    private async void OnAddWeekNotificationClicked(
        object? sender,
        EventArgs e)
    {
        await AddNotificationTimeAsync(
            TimeSpan.FromDays(7));
    }


    private async void OnAddDayNotificationClicked(
        object? sender,
        EventArgs e)
    {
        await AddNotificationTimeAsync(
            TimeSpan.FromDays(1));
    }


    private async void OnAddHourNotificationClicked(
        object? sender,
        EventArgs e)
    {
        await AddNotificationTimeAsync(
            TimeSpan.FromHours(1));
    }


    private async Task AddNotificationTimeAsync(
        TimeSpan offset)
    {
        DateTime? targetDateTime = null;

        if (StartRadioButton.IsChecked)
        {
            targetDateTime = displayStart;
        }

        if (EndRadioButton.IsChecked)
        {
            targetDateTime = displayEnd;
        }

        if (VarRadioButton.IsChecked)
        {
            targetDateTime =
                notificationTimes.Count > 0
                    ? notificationTimes.Min(x => x.Time)
                    : null;
        }

        if (targetDateTime is null)
        {
            string targetName =
                StartRadioButton.IsChecked
                    ? "начала"
                    : EndRadioButton.IsChecked
                        ? "конца"
                        : "уведомления";

            await DisplayAlert(
                "Ошибка",
                $"Сначала выберите дату/время {targetName}.",
                "OK");

            return;
        }

        AddNotificationTime(
            targetDateTime.Value - offset);
    }


    private void OnNotificationTargetChanged(
        object? sender,
        CheckedChangedEventArgs e)
    {
        if (!e.Value ||
            sender is not RadioButton radioButton)
        {
            return;
        }

        selectedBoundary =
            radioButton == EndRadioButton
                ? DisplayBoundary.End
                : DisplayBoundary.Start;
    }


    private void AddNotificationTime(
        DateTime notificationTime)
    {
        if (!notificationTimes.Any(
                x => x.Time == notificationTime))
        {
            NotificationTimeItem item =
                new(notificationTime);

            item.PropertyChanged +=
                OnNotificationTimeItemChanged;

            notificationTimes.Add(item);

            SortNotifications();

            RequestAutoSave();
        }
    }


    private void OnDeleteNotificationClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is Button button &&
            button.CommandParameter is NotificationTimeItem item)
        {
            item.PropertyChanged -=
                OnNotificationTimeItemChanged;

            notificationTimes.Remove(item);

            RequestAutoSave();
        }
    }


    private void OnNotificationTimeTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (sender is Label label &&
            label.BindingContext is NotificationTimeItem item)
        {
            editingNotification = item;

            ShowDateTimePicker(
                item.Time,
                item.Time.TimeOfDay);
        }
    }


    private void OnNotificationTimeItemChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        RequestAutoSave();
    }


    private void SortNotifications()
    {
        List<NotificationTimeItem> sorted =
            notificationTimes
                .OrderBy(x => x.Time)
                .ToList();

        notificationTimes.Clear();

        foreach (NotificationTimeItem item in sorted)
        {
            notificationTimes.Add(item);
        }
    }


    // ============================================================
    // АВТОЗАВЕРШЕНИЕ
    // ============================================================

    private void OnAutoCompleteChanged(
        object? sender,
        CheckedChangedEventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        autoCompleteOnDisplayEnd = e.Value;

        RequestAutoSave();
    }


    private void UpdateAutoCompleteControls()
    {
        bool hasDisplayEnd =
            displayEnd is not null;

        bool hasDisplayStart =
            displayStart is not null;

        AutoCompleteCheckBox.IsVisible =
            hasDisplayEnd;

        AutoCompleteLabel.IsVisible =
            hasDisplayEnd;

        DisplayPeriodGrid.IsVisible =
            hasDisplayEnd ||
            hasDisplayStart;

        if (!hasDisplayEnd)
        {
            autoCompleteOnDisplayEnd = false;
        }

        AutoCompleteCheckBox.IsChecked =
            hasDisplayEnd &&
            autoCompleteOnDisplayEnd;
    }


    private void UpdateDisplayPeriodLabel()
    {
        DisplayPeriodLabel.Text =
            ReminderDisplayFormatter.GetDisplayText(
                displayStart,
                displayEnd);
    }


    // ============================================================
    // ЖИЗНЕННЫЙ ЦИКЛ
    // ============================================================

    protected override void OnAppearing()
    {
        base.OnAppearing();

        UpdateTimerFromDisplayEnd();
        countdownTimer.Start();
    }


    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        countdownTimer.Stop();
        autoSaveCancellation?.Cancel();

        daysSnapCancellation?.Cancel();
        hoursSnapCancellation?.Cancel();
        minutesSnapCancellation?.Cancel();

        if (!isDeleting)
        {
            RequestSave();
        }
    }


    // ============================================================
    // БЫСТРЫЕ КНОПКИ ВРЕМЕНИ
    // ============================================================

    private void settime900(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(9, 0, 0);
    }


    private void settime2100(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(21, 0, 0);
    }


    private void settime1500(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(15, 0, 0);
    }


    private void settime300(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(3, 0, 0);
    }


    private void add1day(
        object? sender,
        EventArgs e)
    {
        OverlayDatePicker.Date =
            OverlayDatePicker.Date.AddDays(1);
    }


    private void rem1day(
        object? sender,
        EventArgs e)
    {
        OverlayDatePicker.Date =
            OverlayDatePicker.Date.AddDays(-1);
    }


    // ============================================================
    // ТАЙМЕР
    // ============================================================

    private void InitializeTimerWheels()
    {
        CreateWheel(
            DaysWheelLayout,
            100);

        CreateWheel(
            HoursWheelLayout,
            24);

        CreateWheel(
            MinutesWheelLayout,
            60);

        UpdateWheelVisuals(
            DaysWheelLayout,
            timerDays);

        UpdateWheelVisuals(
            HoursWheelLayout,
            timerHours);

        UpdateWheelVisuals(
            MinutesWheelLayout,
            timerMinutes);
    }


    private static void CreateWheel(
        VerticalStackLayout layout,
        int count)
    {
        // Верхнее пустое пространство.
        // Благодаря ему первый элемент тоже может оказаться
        // точно в центре колеса.

        layout.Children.Add(
            new BoxView
            {
                HeightRequest = TimerWheelTopPadding,
                InputTransparent = true
            });


        for (int i = 0; i < count; i++)
        {
            Label label = new()
            {
                Text = i.ToString("00"),

                HeightRequest =
                    TimerWheelItemHeight,

                FontSize = 34,

                HorizontalTextAlignment =
                    TextAlignment.Center,

                VerticalTextAlignment =
                    TextAlignment.Center,

                TextColor =
                    Color.FromArgb("#646D77"),

                InputTransparent = true
            };

            layout.Children.Add(label);
        }


        // Нижнее пустое пространство.

        layout.Children.Add(
            new BoxView
            {
                HeightRequest = TimerWheelTopPadding,
                InputTransparent = true
            });
    }


    private static int GetSelectedWheelIndex(
        double scrollY,
        int count)
    {
        int index =
            (int)Math.Round(
                scrollY / TimerWheelItemHeight);

        return Math.Clamp(
            index,
            0,
            count - 1);
    }


    private void UpdateWheelVisuals(
        VerticalStackLayout layout,
        int selectedIndex)
    {
        for (int i = 0;
             i < layout.Children.Count;
             i++)
        {
            if (layout.Children[i] is not Label label)
            {
                continue;
            }

            // Первый элемент StackLayout — верхний spacer.
            int valueIndex = i - 1;

            if (valueIndex < 0)
            {
                continue;
            }

            double distance =
                Math.Abs(
                    valueIndex - selectedIndex);


            if (valueIndex == selectedIndex)
            {
                label.FontSize = 38;

                label.FontAttributes =
                    FontAttributes.Bold;

                label.TextColor =
                    Colors.White;

                label.Opacity = 1.0;
            }
            else if (distance == 1)
            {
                label.FontSize = 34;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#737C86");

                label.Opacity = 0.9;
            }
            else if (distance == 2)
            {
                label.FontSize = 30;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#525A63");

                label.Opacity = 0.65;
            }
            else
            {
                label.FontSize = 28;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#414850");

                label.Opacity = 0.35;
            }
        }
    }


    // ============================================================
    // ПРОКРУТКА ДНЕЙ
    // ============================================================

    private void OnDaysWheelScrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        pendingTimerDays =
            GetSelectedWheelIndex(
                e.ScrollY,
                100);

        UpdateWheelVisuals(
            DaysWheelLayout,
            pendingTimerDays);

        if (!isTimerWheelProgrammaticScroll)
        {
            ScheduleDaysSnap();
        }
    }


    // ============================================================
    // ПРОКРУТКА ЧАСОВ
    // ============================================================

    private void OnHoursWheelScrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        pendingTimerHours =
            GetSelectedWheelIndex(
                e.ScrollY,
                24);

        UpdateWheelVisuals(
            HoursWheelLayout,
            pendingTimerHours);

        if (!isTimerWheelProgrammaticScroll)
        {
            ScheduleHoursSnap();
        }
    }


    // ============================================================
    // ПРОКРУТКА МИНУТ
    // ============================================================

    private void OnMinutesWheelScrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        pendingTimerMinutes =
            GetSelectedWheelIndex(
                e.ScrollY,
                60);

        UpdateWheelVisuals(
            MinutesWheelLayout,
            pendingTimerMinutes);

        if (!isTimerWheelProgrammaticScroll)
        {
            ScheduleMinutesSnap();
        }
    }


    // ============================================================
    // SNAP ДНЕЙ
    // ============================================================

    private void ScheduleDaysSnap()
    {
        daysSnapCancellation?.Cancel();

        daysSnapCancellation =
            new CancellationTokenSource();

        _ = SnapDaysAsync(
            daysSnapCancellation.Token);
    }


    private async Task SnapDaysAsync(
        CancellationToken token)
    {
        try
        {
            await Task.Delay(
                120,
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            await SnapWheelAsync(
                DaysWheel,
                pendingTimerDays,
                token);
        }
        catch (TaskCanceledException)
        {
        }
    }


    // ============================================================
    // SNAP ЧАСОВ
    // ============================================================

    private void ScheduleHoursSnap()
    {
        hoursSnapCancellation?.Cancel();

        hoursSnapCancellation =
            new CancellationTokenSource();

        _ = SnapHoursAsync(
            hoursSnapCancellation.Token);
    }


    private async Task SnapHoursAsync(
        CancellationToken token)
    {
        try
        {
            await Task.Delay(
                120,
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            await SnapWheelAsync(
                HoursWheel,
                pendingTimerHours,
                token);
        }
        catch (TaskCanceledException)
        {
        }
    }


    // ============================================================
    // SNAP МИНУТ
    // ============================================================

    private void ScheduleMinutesSnap()
    {
        minutesSnapCancellation?.Cancel();

        minutesSnapCancellation =
            new CancellationTokenSource();

        _ = SnapMinutesAsync(
            minutesSnapCancellation.Token);
    }


    private async Task SnapMinutesAsync(
        CancellationToken token)
    {
        try
        {
            await Task.Delay(
                120,
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            await SnapWheelAsync(
                MinutesWheel,
                pendingTimerMinutes,
                token);
        }
        catch (TaskCanceledException)
        {
        }
    }


    // ============================================================
    // ОБЩИЙ SNAP
    // ============================================================

    private async Task SnapWheelAsync(
        ScrollView wheel,
        int index,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        double targetY =
            index * TimerWheelItemHeight;

        isTimerWheelProgrammaticScroll = true;

        try
        {
            await wheel.ScrollToAsync(
                0,
                targetY,
                true);
        }
        finally
        {
            isTimerWheelProgrammaticScroll = false;
        }
    }


    // ============================================================
    // ОТКРЫТИЕ ВЫБОРА ТАЙМЕРА
    // ============================================================

    private async void OnTimerDisplayTapped(
        object? sender,
        TappedEventArgs e)
    {
        UpdateTimerFromDisplayEnd();

        pendingTimerDays = timerDays;
        pendingTimerHours = timerHours;
        pendingTimerMinutes = timerMinutes;

        TimerDurationOverlay.IsVisible = true;

        await Task.Delay(50);

        isTimerWheelProgrammaticScroll = true;

        try
        {
            await DaysWheel.ScrollToAsync(
                0,
                pendingTimerDays *
                    TimerWheelItemHeight,
                false);

            await HoursWheel.ScrollToAsync(
                0,
                pendingTimerHours *
                    TimerWheelItemHeight,
                false);

            await MinutesWheel.ScrollToAsync(
                0,
                pendingTimerMinutes *
                    TimerWheelItemHeight,
                false);
        }
        finally
        {
            isTimerWheelProgrammaticScroll = false;
        }

        UpdateWheelVisuals(
            DaysWheelLayout,
            pendingTimerDays);

        UpdateWheelVisuals(
            HoursWheelLayout,
            pendingTimerHours);

        UpdateWheelVisuals(
            MinutesWheelLayout,
            pendingTimerMinutes);
    }


    // ============================================================
    // СОХРАНЕНИЕ ТАЙМЕРА
    // ============================================================

    private void OnSaveTimerClicked(
        object? sender,
        EventArgs e)
    {
        TimeSpan duration = new(
            pendingTimerDays,
            pendingTimerHours,
            pendingTimerMinutes,
            0);

        // Таймер — это только представление даты окончания, поэтому при его
        // изменении переносим дату окончания на указанное время от текущей минуты.
        displayEnd = GetCurrentMinute().Add(duration);
        selectedBoundary = DisplayBoundary.End;

        isUpdatingPickers = true;
        OverlayDatePicker.Date = displayEnd.Value.Date;
        OverlayTimePicker.Time = displayEnd.Value.TimeOfDay;
        isUpdatingPickers = false;

        UpdateSelectedDateTimeLabels();
        UpdateDisplayPeriodLabel();
        UpdateAutoCompleteControls();
        UpdateTimerFromDisplayEnd();
        RequestAutoSave();

        TimerDurationOverlay.IsVisible = false;
    }


    private void OnCountdownTimerTick(
        object? sender,
        EventArgs e)
    {
        if (DateTimePickerOverlay.IsVisible)
        {
            UpdateTimerFromDateTimePicker();
            return;
        }

        UpdateTimerFromDisplayEnd();
    }


    private void UpdateTimerFromDateTimePicker()
    {
        // Date/time picker changes are only a preview until the user saves
        // them. Reflect an edited end date in the visual-only timer without
        // changing the reminder or affecting notification scheduling.
        if (editingNotification is not null ||
            selectedBoundary != DisplayBoundary.End)
        {
            UpdateTimerFromDisplayEnd();
            return;
        }

        DateTime selectedEnd =
            OverlayDatePicker.Date + OverlayTimePicker.Time;

        UpdateTimerDisplay(selectedEnd - DateTime.Now);
    }


    private void UpdateTimerFromDisplayEnd()
    {
        if (displayEnd is null)
        {
            SetTimerDisplay(TimeSpan.Zero);
            return;
        }

        UpdateTimerDisplay(displayEnd.Value - DateTime.Now);
    }


    private void UpdateTimerDisplay(TimeSpan remaining)
    {

        if (remaining <= TimeSpan.Zero)
        {
            SetTimerDisplay(TimeSpan.Zero);
            return;
        }

        // Колесо задаёт время с точностью до минуты. Округление вверх не
        // позволяет показывать на минуту меньше сразу после обновления.
        TimeSpan roundedRemaining = TimeSpan.FromMinutes(
            Math.Ceiling(remaining.TotalMinutes));

        SetTimerDisplay(roundedRemaining);
    }


    private static DateTime GetCurrentMinute()
    {
        DateTime now = DateTime.Now;

        return new DateTime(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            0,
            now.Kind);
    }


    private void SetTimerDisplay(TimeSpan duration)
    {
        timerDays = duration.Days;
        timerHours = duration.Hours;
        timerMinutes = duration.Minutes;

        UpdateTimerDisplay();
    }


    private void UpdateTimerDisplay()
    {
        TimerDaysDisplayLabel.Text =
            timerDays.ToString("00");

        TimerHoursDisplayLabel.Text =
            timerHours.ToString("00");

        TimerMinutesDisplayLabel.Text =
            timerMinutes.ToString("00");
    }
}