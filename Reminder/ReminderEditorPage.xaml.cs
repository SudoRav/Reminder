using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Reminder;

public partial class ReminderEditorPage : ContentPage
{
    // ============================================================
    // ОСНОВНЫЕ ТИПЫ
    // ============================================================

    private enum DisplayBoundary
    {
        Start,
        End,
    }

    private enum NotificationReference
    {
        Now,
        Start,
        End,
    }

    private enum NotificationDirection
    {
        Through,
        Before,
    }

    private enum NotificationUnit
    {
        Minute,
        Hour,
        Day,
    }


    // ============================================================
    // ОСНОВНЫЕ ПОЛЯ
    // ============================================================

    private readonly ReminderItem? reminder;
    private int reminderId;

    private DateTime? displayStart;
    private DateTime? displayEnd;
    private int group = 3;

    private readonly ObservableCollection<NotificationTimeItem>
        notificationTimes;

    private bool isSavingNotificationTimerDuration;
    private DisplayBoundary selectedBoundary;

    private bool isUpdatingPickers;

    private const int ReminderTextMaxLength = 1024;

    private bool autoCompleteOnDisplayEnd;

    private bool showInNotificationCenter = true;

    // Не null, когда открыт Date/TimePicker
    // конкретного NotificationTimeItem.
    private NotificationTimeItem? editingNotification;

    private bool isInitializing;

    private CancellationTokenSource? autoSaveCancellation;

    private readonly IDispatcherTimer countdownTimer;

    private bool isAutoSaveEnabled = false;

    private bool isClosing;

    // true, пока идёт сохранение (защита от двойного нажатия).
    private bool isSaving;


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

    private bool isTimerWheelProgrammaticScroll;


    // ============================================================
    // ДОБАВЛЕНИЕ ОПОВЕЩЕНИЯ
    // ============================================================

    private const double NotificationWheelItemHeight = 60;
    private const double NotificationWheelTopPadding = 90;

    // Когда?
    // По умолчанию: Сейчас.
    private NotificationReference notificationReference =
        NotificationReference.Now;

    // Время?
    // 0 = Через
    // 1 = За
    private int notificationDirectionIndex;

    // Отображаемое значение 0..99.
    // По умолчанию — 1 (показывается как "01").
    private int notificationAmount = 1;

    // 0 = Мин
    // 1 = Час
    // 2 = Ден
    private int notificationUnitIndex = 1;

    private int pendingNotificationDirectionIndex;
    private int pendingNotificationAmount;
    private int pendingNotificationUnitIndex;

    private CancellationTokenSource?
        notificationDirectionSnapCancellation;

    private CancellationTokenSource?
        notificationAmountSnapCancellation;

    private CancellationTokenSource?
        notificationUnitSnapCancellation;

    private bool isNotificationWheelProgrammaticScroll;


    // ============================================================
    // СОБЫТИЯ
    // ============================================================

    public event Func<ReminderItem, Task>? SaveRequested;

    public event EventHandler? DeleteRequested;


    // ============================================================
    // КОНСТРУКТОР
    // ============================================================

    public ReminderEditorPage(
        ReminderItem? reminder = null)
    {
        InitializeComponent();

        SetNotificationTimerButtonVisible(false);

        countdownTimer =
            Dispatcher.CreateTimer();

        countdownTimer.Interval =
            TimeSpan.FromSeconds(1);

        countdownTimer.Tick +=
            OnCountdownTimerTick;

        InitializeTimerWheels();

        InitializeNotificationWheels();

        this.reminder =
            reminder;

        ReminderTextEditor.Text =
            reminder?.Text ?? string.Empty;

        UpdateReminderCharacterLimit();

        displayStart =
            reminder?.DisplayStart;

        displayEnd =
            reminder?.DisplayEnd;

        group =
            reminder?.Group ?? 3;

        autoCompleteOnDisplayEnd =
            reminder?.AutoCompleteOnDisplayEnd ?? false;

        showInNotificationCenter =
            reminder?.ShowInNotificationCenter ?? true;

        if (reminder is not null)
        {
            reminder.NormalizeNotificationSettings();
        }

        notificationTimes =
            new ObservableCollection<NotificationTimeItem>(
                reminder?.NotificationTimeSettings
                    .OrderBy(x => x.Time)
                    .Select(
                        x => new NotificationTimeItem(x))
                ?? Enumerable.Empty<NotificationTimeItem>());


        foreach (NotificationTimeItem item
                 in notificationTimes)
        {
            item.PropertyChanged +=
                OnNotificationTimeItemChanged;
        }


        NotificationTimesCollectionView.ItemsSource =
            notificationTimes;


        DeleteButton.IsVisible =
            reminder is not null;


        selectedBoundary =
            DisplayBoundary.End;


        UpdateNotificationReferenceButtons();

        UpdateDisplayPeriodLabel();

        UpdateAutoCompleteControls();

        UpdateGroupButtonVisual();

        UpdateTimerFromCurrentTarget();


        isInitializing = false;

        countdownTimer.Start();
    }

    private void UpdateReminderCharacterLimit()
    {
        int length =
            ReminderTextEditor.Text?.Length ?? 0;

        ReminderCharacterLimit.Text =
            $"{length} / {ReminderTextMaxLength}";
    }

    private void UpdateGroupButtonVisual()
    {
        switch (group)
        {
            case 1:
                GroupButton.BackgroundColor =
                    Color.FromArgb("#FF6B6B");

                GroupButton.BorderWidth = 0;
                GroupButton.BorderColor = Colors.Transparent;
                break;

            case 2:
                GroupButton.BackgroundColor =
                    Color.FromArgb("#FFD93D");

                GroupButton.BorderWidth = 0;
                GroupButton.BorderColor = Colors.Transparent;
                break;

            case 3:
                GroupButton.BackgroundColor =
                    Colors.Transparent;

                GroupButton.BorderColor =
                    Application.Current?.RequestedTheme == AppTheme.Dark
                        ? Color.FromArgb("#B0B0B0")
                        : Color.FromArgb("#808080");

                GroupButton.BorderWidth = 1;
                break;

            case 4:
                GroupButton.BackgroundColor =
                    Color.FromArgb("#4D96FF");

                GroupButton.BorderWidth = 0;
                GroupButton.BorderColor = Colors.Transparent;
                break;

            default:
                group = 3;

                GroupButton.BackgroundColor =
                    Colors.Transparent;

                GroupButton.BorderColor =
                    Color.FromArgb("#808080");

                GroupButton.BorderWidth = 1;
                break;
        }
    }

    private void OnGroupSelectionOverlayTapped(
        object? sender,
        TappedEventArgs e)
    {
        GroupSelectionOverlay.IsVisible = false;
    }

    private void OnGroupButtonClicked(
        object? sender,
        EventArgs e)
    {
        GroupSelectionOverlay.IsVisible = true;
    }

    private void OnEditorGroupButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (!int.TryParse(
                button.CommandParameter?.ToString(),
                out int selectedGroup))
        {
            return;
        }

        if (selectedGroup < 1 || selectedGroup > 4)
        {
            return;
        }

        group = selectedGroup;

        UpdateGroupButtonVisual();

        GroupSelectionOverlay.IsVisible = false;

        RequestAutoSave();
    }


    private static readonly Color TimerExpiredColor =
        Color.FromArgb("#D98282");

    private static readonly Color TimerNormalLightColor =
        Color.FromArgb("#20242A");

    private static readonly Color TimerNormalDarkColor =
        Colors.White;


    // ============================================================
    // ДАТА / ВРЕМЯ
    // ============================================================

    private void OnStartClicked(
        object? sender,
        EventArgs e)
    {
        editingNotification = null;
        DisplayPeriodLabel.IsVisible =
            true;

        selectedBoundary =
            DisplayBoundary.Start;

        DateTime initialDate;
        TimeSpan initialTime;

        if (displayStart is DateTime savedStart)
        {
            initialDate =
                savedStart.Date;

            initialTime =
                savedStart.TimeOfDay;
        }
        else if (displayEnd is DateTime savedEnd)
        {
            initialDate =
                savedEnd.Date.AddDays(0);

            initialTime =
                TimeSpan.Zero;
        }
        else
        {
            initialDate =
                DateTime.Today;

            initialTime =
                TimeSpan.Zero;
        }

        ShowDateTimePicker(
            initialDate,
            initialTime);
    }


    private void OnEndClicked(
        object? sender,
        EventArgs e)
    {
        // Сбрасываем редактируемое оповещение,
        // чтобы кнопка 🔁 скрылась.
        editingNotification = null;

        DisplayPeriodLabel.IsVisible =
            true;

        selectedBoundary =
            DisplayBoundary.End;

        DateTime initialDate;
        TimeSpan initialTime;

        if (displayEnd is DateTime savedEnd)
        {
            initialDate =
                savedEnd.Date;

            initialTime =
                savedEnd.TimeOfDay;
        }
        else if (displayStart is DateTime savedStart)
        {
            initialDate =
                savedStart.Date.AddDays(0);

            initialTime =
                new TimeSpan(23, 0, 0);
        }
        else
        {
            initialDate =
                DateTime.Today.AddDays(0);

            initialTime =
                new TimeSpan(23, 0, 0);
        }

        ShowDateTimePicker(
            initialDate,
            initialTime);
    }


    private void ShowDateTimePicker(
        DateTime initialDate,
        TimeSpan initialTime)
    {
        SetNotificationTimerButtonVisible(
            editingNotification is not null);

        isUpdatingPickers =
            true;

        OverlayDatePicker.Date =
            initialDate;

        OverlayTimePicker.Time =
            initialTime;

        isUpdatingPickers =
            false;

        UpdateSelectedDateTimeLabels();

        UpdateTimerFromDateTimePicker();

        DateTimePickerOverlay.IsVisible =
            true;
    }


    private void SetNotificationTimerButtonVisible(
    bool visible)
    {
        NotificationTimerButton.IsVisible =
            visible;

        if (NotificationTimerButton.Parent is not Grid grid)
        {
            return;
        }

        grid.ColumnDefinitions.Clear();

        if (visible)
        {
            // Три колонки: * , * , Auto
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Star });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Star });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            grid.ColumnSpacing = 5;

            Grid.SetColumn(
                NotificationTimerButton,
                2);

            Grid.SetColumnSpan(
                NotificationTimerButton,
                1);
        }
        else
        {
            // Две колонки: * , *
            // Лишнего ColumnSpacing справа больше нет.
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Star });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Star });

            grid.ColumnSpacing = 5;

            // Скрытую кнопку уводим в существующую колонку,
            // чтобы она не «расширяла» сетку.
            Grid.SetColumn(
                NotificationTimerButton,
                1);

            Grid.SetColumnSpan(
                NotificationTimerButton,
                1);
        }
    }

    private void OnDateRowTapped(
        object? sender,
        TappedEventArgs e)
    {
        _ = OpenPickerAsync(
            OverlayDatePicker);
    }


    private void OnTimeRowTapped(
        object? sender,
        TappedEventArgs e)
    {
        _ = OpenPickerAsync(
            OverlayTimePicker);
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
            e.PropertyName ==
            TimePicker.TimeProperty.PropertyName)
        {
            UpdateSelectedDateTimeLabels();

            UpdateTimerFromDateTimePicker();
        }
    }


    private void OnCancelDateTimeClicked(
        object? sender,
        EventArgs e)
    {
        editingNotification =
            null;

        DateTimePickerOverlay.IsVisible =
            false;

        UpdateTimerFromSelectedBoundary();
    }


    private void OnSaveDateTimeClicked(
        object? sender,
        EventArgs e)
    {
        ApplySelectedDateTime();

        DateTimePickerOverlay.IsVisible =
            false;
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


    private static async Task OpenPickerAsync(
        View picker)
    {
        await Task.Delay(50);

        bool focused =
            await picker.Dispatcher.DispatchAsync(
                picker.Focus);

        if (!focused)
        {
            OpenPickerWithIsOpenProperty(
                picker);
        }
    }


    private static bool OpenPickerWithIsOpenProperty(
        View picker)
    {
        PropertyInfo? isOpenProperty =
            picker.GetType()
                .GetProperty("IsOpen");

        if (isOpenProperty?.PropertyType !=
            typeof(bool) ||
            !isOpenProperty.CanWrite)
        {
            return false;
        }

        isOpenProperty.SetValue(
            picker,
            true);

        return true;
    }


    private void ApplySelectedDateTime()
    {
        DateTime value =
            OverlayDatePicker.Date +
            OverlayTimePicker.Time;


        // ========================================================
        // NotificationTimeItem
        // ========================================================

        if (editingNotification is not null)
        {
            editingNotification.Time =
                value;

            SortNotifications();

            NotificationTimesCollectionView.ItemsSource =
                null;

            NotificationTimesCollectionView.ItemsSource =
                notificationTimes;

            editingNotification =
                null;

            UpdateTimerFromSelectedBoundary();

            RequestAutoSave();

            return;
        }


        // ========================================================
        // Start
        // ========================================================

        if (selectedBoundary ==
            DisplayBoundary.Start)
        {
            displayStart =
                value;
        }


        // ========================================================
        // End
        // ========================================================

        else
        {
            bool hadDisplayEnd =
                displayEnd is not null;

            displayEnd =
                value;

            if (!hadDisplayEnd)
            {
                autoCompleteOnDisplayEnd =
                    false;
            }
        }

        UpdateDisplayPeriodLabel();

        UpdateAutoCompleteControls();

        UpdateNotificationReferenceButtons();

        UpdateNotificationDirectionWheel();

        UpdateTimerFromSelectedBoundary();

        RequestAutoSave();
    }



    // ============================================================
    // АВТОСОХРАНЕНИЕ
    // ============================================================

    private void OnReminderChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (ReminderTextEditor.Text is { Length: > ReminderTextMaxLength } text)
        {
            // Обрезаем до лимита.
            ReminderTextEditor.Text =
                text[..ReminderTextMaxLength];

            // После программного изменения TextChanged
            // вызовется повторно — выйдем из него на следующем проходе.
            return;
        }

        UpdateReminderCharacterLimit();

        RequestAutoSave();
    }


    private Task NotifySaveRequestedAsync(
        ReminderItem editedReminder)
    {
        Func<ReminderItem, Task>? saveRequested =
            SaveRequested;

        return saveRequested is null
            ? Task.CompletedTask
            : saveRequested(editedReminder);
    }


    private ReminderItem CreateReminderForSave(
        string text)
    {
        return new ReminderItem
        {
            Id =
                reminder?.Id ?? 0,

            Text =
                text,

            DisplayStart =
                displayStart,

            DisplayEnd =
                displayEnd,

            Group =
                group,

            AutoCompleteOnDisplayEnd =
                autoCompleteOnDisplayEnd,

            ShowInNotificationCenter =
                showInNotificationCenter,

            NotificationTimes =
                notificationTimes
                    .Select(x => x.Time)
                    .Order()
                    .ToList(),

            NotificationTimeSettings =
                notificationTimes
                    .OrderBy(x => x.Time)
                    .Select(x => x.ToSettings())
                    .ToList(),
        };
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
            await Task.Delay(
                700,
                token);
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
            ReminderTextEditor.Text?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                _ = NotifySaveRequestedAsync(
                    CreateReminderForSave(text));
            });
    }


    private async void OnDeleteClicked(
        object? sender,
        EventArgs e)
    {
        if (reminder is null)
        {
            return;
        }

        isClosing =
            true;

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
            ReminderTextEditor.Text?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlert(
                "Ошибка",
                "Введите текст напоминания.",
                "OK");

            return;
        }

        await SaveAndCloseAsync(text);
    }



    // ============================================================
    // ДОБАВЛЕНИЕ ОПОВЕЩЕНИЯ
    // ============================================================

    private async void OnAddNotificationClicked(
        object? sender,
        EventArgs e)
    {
        UpdateNotificationDirectionWheel();

        notificationReference =
            NotificationReference.Now;

        notificationDirectionIndex =
            0; // Через

        // По умолчанию показываем "01".
        notificationAmount =
            1;

        notificationUnitIndex =
            1; // Час

        pendingNotificationDirectionIndex =
            0;

        pendingNotificationAmount =
            1;

        pendingNotificationUnitIndex =
            1;

        UpdateNotificationReferenceButtons();

        UpdateNotificationWheelVisuals();

        NotificationAddOverlay.IsVisible =
            true;

        await Task.Delay(50);

        await PositionNotificationWheelsAsync();
    }


    private async Task PositionNotificationWheelsAsync()
    {
        isNotificationWheelProgrammaticScroll =
            true;

        try
        {
            await NotificationDirectionWheel.ScrollToAsync(
                0,
                pendingNotificationDirectionIndex *
                    NotificationWheelItemHeight,
                false);


            // pendingNotificationAmount хранит именно
            // отображаемое значение (0..99),
            // и оно же является индексом в колесе,
            // т.к. элементы идут 00, 01, 02, ...
            await NotificationAmountWheel.ScrollToAsync(
                0,
                pendingNotificationAmount *
                    NotificationWheelItemHeight,
                false);


            await NotificationUnitWheel.ScrollToAsync(
                0,
                pendingNotificationUnitIndex *
                    NotificationWheelItemHeight,
                false);
        }
        finally
        {
            isNotificationWheelProgrammaticScroll =
                false;
        }

        UpdateNotificationWheelVisuals();
    }


    private void OnCancelNotificationClicked(
        object? sender,
        EventArgs e)
    {
        NotificationAddOverlay.IsVisible =
            false;
    }


    private async void OnSaveNotificationClicked(
        object? sender,
        EventArgs e)
    {
        DateTime baseDateTime;

        // ========================================================
        // ОПРЕДЕЛЯЕМ БАЗОВОЕ ВРЕМЯ
        // ========================================================

        switch (notificationReference)
        {
            case NotificationReference.Now:

                baseDateTime =
                    DateTime.Now;

                break;


            case NotificationReference.Start:

                if (displayStart is not DateTime start)
                {
                    await DisplayAlert(
                        "Ошибка",
                        "Сначала задайте начало напоминания.",
                        "OK");

                    return;
                }

                baseDateTime =
                    start;

                break;


            case NotificationReference.End:

                if (displayEnd is not DateTime end)
                {
                    await DisplayAlert(
                        "Ошибка",
                        "Сначала задайте конец напоминания.",
                        "OK");

                    return;
                }

                baseDateTime =
                    end;

                break;


            default:
                return;
        }


        // ========================================================
        // ОПРЕДЕЛЯЕМ СМЕЩЕНИЕ
        // ========================================================

        TimeSpan offset =
            GetNotificationOffset();


        // ========================================================
        // ЧЕРЕЗ / ЗА
        // ========================================================

        if (notificationDirectionIndex == 1 &&
            displayStart is null &&
            displayEnd is null)
        {
            notificationDirectionIndex = 0;
            pendingNotificationDirectionIndex = 0;
        }

        DateTime notificationTime =
            notificationDirectionIndex == 0
                ? baseDateTime + offset
                : baseDateTime - offset;


        AddNotificationTime(
            notificationTime);


        NotificationAddOverlay.IsVisible =
            false;
    }


    private void AddNotificationTime(
    DateTime notificationTime)
    {
        if (notificationTimes.Any(
                x => x.Time == notificationTime))
        {
            return;
        }

        NotificationTimeItem item =
            new(notificationTime);

        item.PropertyChanged +=
            OnNotificationTimeItemChanged;

        notificationTimes.Add(
            item);

        SortNotifications();

        RequestAutoSave();
    }

    private TimeSpan GetNotificationOffset()
    {
        // notificationAmount — это отображаемое число (0..99),
        // а не индекс в массиве.
        return notificationUnitIndex switch
        {
            // 0 = минуты
            0 =>
                TimeSpan.FromMinutes(
                    notificationAmount),

            // 1 = часы
            1 =>
                TimeSpan.FromHours(
                    notificationAmount),

            // 2 = дни
            2 =>
                TimeSpan.FromDays(
                    notificationAmount),

            _ =>
                TimeSpan.Zero
        };
    }



    // ============================================================
    // КОГДА?
    // ============================================================

    private void OnNotificationNowClicked(object? sender, EventArgs e)
    {
        notificationReference = NotificationReference.Now;

        notificationDirectionIndex = 0;
        pendingNotificationDirectionIndex = 0;

        UpdateNotificationReferenceButtons();
        UpdateNotificationWheelVisuals();

        _ = PositionNotificationWheelsAsync();
    }


    private void OnNotificationStartClicked(object? sender, EventArgs e)
    {
        notificationReference = NotificationReference.Start;

        // Для "Начало" автоматически выбираем "За"
        if (displayStart is not null || displayEnd is not null)
        {
            notificationDirectionIndex = 1;
            pendingNotificationDirectionIndex = 1;
        }

        UpdateNotificationReferenceButtons();
        UpdateNotificationWheelVisuals();

        _ = PositionNotificationWheelsAsync();
    }


    private void OnNotificationEndClicked(object? sender, EventArgs e)
    {
        notificationReference = NotificationReference.End;

        // Для "Конец" автоматически выбираем "За"
        if (displayStart is not null || displayEnd is not null)
        {
            notificationDirectionIndex = 1;
            pendingNotificationDirectionIndex = 1;
        }

        UpdateNotificationReferenceButtons();
        UpdateNotificationWheelVisuals();

        _ = PositionNotificationWheelsAsync();
    }


    private void UpdateNotificationReferenceButtons()
    {
        bool hasDisplayStart =
            displayStart is not null;

        bool hasDisplayEnd =
            displayEnd is not null;


        // ========================================================
        // ВИДИМОСТЬ
        // ========================================================

        NotificationNowButton.IsVisible =
            true;

        NotificationStartButton.IsVisible =
            hasDisplayStart;

        NotificationEndButton.IsVisible =
            hasDisplayEnd;


        // ========================================================
        // ДИНАМИЧЕСКАЯ РАЗМЕТКА GRID
        // ========================================================
        //
        // В XAML изначально:
        //
        //     ColumnDefinitions="*,*,*"
        //
        // IsVisible=False скрывает только кнопку, но не колонку.
        //
        // Поэтому здесь мы полностью перестраиваем колонки.
        //
        // 3 кнопки:
        //     *,*,*
        //
        // 2 кнопки:
        //     *,*
        //
        // 1 кнопка:
        //     *
        // ========================================================

        if (NotificationNowButton.Parent is Grid grid)
        {
            int visibleButtonCount =
                1 +
                (hasDisplayStart ? 1 : 0) +
                (hasDisplayEnd ? 1 : 0);


            grid.ColumnDefinitions.Clear();


            for (int i = 0;
                 i < visibleButtonCount;
                 i++)
            {
                grid.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width =
                            GridLength.Star
                    });
            }


            grid.ColumnSpacing =
                visibleButtonCount > 1
                    ? 4
                    : 0;


            // Сейчас всегда первая кнопка.
            Grid.SetColumn(
                NotificationNowButton,
                0);


            int nextColumn =
                1;


            // Начало.
            if (hasDisplayStart)
            {
                Grid.SetColumn(
                    NotificationStartButton,
                    nextColumn);

                nextColumn++;
            }
            else
            {
                // Скрытая кнопка не влияет на layout.
                Grid.SetColumn(
                    NotificationStartButton,
                    0);
            }


            // Конец.
            if (hasDisplayEnd)
            {
                Grid.SetColumn(
                    NotificationEndButton,
                    nextColumn);
            }
            else
            {
                // Скрытая кнопка не влияет на layout.
                Grid.SetColumn(
                    NotificationEndButton,
                    0);
            }
        }


        // ========================================================
        // ЦВЕТА
        // ========================================================

        bool isDark =
            Application.Current?.RequestedTheme ==
            AppTheme.Dark;


        Color selectedBackground =
            isDark
                ? Color.FromArgb("#2A3A4A")
                : Color.FromArgb("#DCE8F8");


        Color normalBackground =
            Colors.Transparent;


        Color selectedTextColor =
            isDark
                ? Colors.White
                : Color.FromArgb("#20242A");


        Color normalTextColor =
            isDark
                ? Colors.White
                : Color.FromArgb("#20242A");


        // ========================================================
        // ФОН
        // ========================================================

        NotificationNowButton.BackgroundColor =
            notificationReference ==
            NotificationReference.Now
                ? selectedBackground
                : normalBackground;


        NotificationStartButton.BackgroundColor =
            notificationReference ==
            NotificationReference.Start
                ? selectedBackground
                : normalBackground;


        NotificationEndButton.BackgroundColor =
            notificationReference ==
            NotificationReference.End
                ? selectedBackground
                : normalBackground;


        // ========================================================
        // ЦВЕТ ТЕКСТА
        // ========================================================

        NotificationNowButton.TextColor =
            notificationReference ==
            NotificationReference.Now
                ? selectedTextColor
                : normalTextColor;


        NotificationStartButton.TextColor =
            notificationReference ==
            NotificationReference.Start
                ? selectedTextColor
                : normalTextColor;


        NotificationEndButton.TextColor =
            notificationReference ==
            NotificationReference.End
                ? selectedTextColor
                : normalTextColor;


        // ========================================================
        // ЖИРНОСТЬ
        // ========================================================

        NotificationNowButton.FontAttributes =
            notificationReference ==
            NotificationReference.Now
                ? FontAttributes.Bold
                : FontAttributes.None;


        NotificationStartButton.FontAttributes =
            notificationReference ==
            NotificationReference.Start
                ? FontAttributes.Bold
                : FontAttributes.None;


        NotificationEndButton.FontAttributes =
            notificationReference ==
            NotificationReference.End
                ? FontAttributes.Bold
                : FontAttributes.None;
    }



    // ============================================================
    // КОЛЕСО «ЧЕРЕЗ / ЗА»
    // ============================================================

    private void OnNotificationDirectionWheelScrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        int itemCount =
            displayStart is not null ||
            displayEnd is not null
                ? 2
                : 1;

        pendingNotificationDirectionIndex =
            GetNotificationWheelIndex(
                e.ScrollY,
                itemCount);

        notificationDirectionIndex =
            pendingNotificationDirectionIndex;

        UpdateNotificationWheelVisuals();

        if (!isNotificationWheelProgrammaticScroll)
        {
            notificationDirectionSnapCancellation?.Cancel();

            notificationDirectionSnapCancellation =
                new CancellationTokenSource();

            _ = SnapNotificationDirectionAsync(
                notificationDirectionSnapCancellation.Token);
        }
    }


    private async Task SnapNotificationDirectionAsync(
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

            await SnapNotificationWheelAsync(
                NotificationDirectionWheel,
                pendingNotificationDirectionIndex,
                token);
        }
        catch (TaskCanceledException)
        {
        }
    }



    // ============================================================
    // КОЛЕСО ЧИСЛА 00..99
    // ============================================================

    private void OnNotificationAmountWheelScrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        // Индекс в колесе совпадает с отображаемым значением,
        // т.к. элементы идут 00, 01, 02, ... 99.
        pendingNotificationAmount =
            GetNotificationWheelIndex(
                e.ScrollY,
                100);


        notificationAmount =
            pendingNotificationAmount;


        UpdateNotificationWheelVisuals();


        if (!isNotificationWheelProgrammaticScroll)
        {
            notificationAmountSnapCancellation?.Cancel();

            notificationAmountSnapCancellation =
                new CancellationTokenSource();

            _ = SnapNotificationAmountAsync(
                notificationAmountSnapCancellation.Token);
        }
    }


    private async Task SnapNotificationAmountAsync(
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

            await SnapNotificationWheelAsync(
                NotificationAmountWheel,
                pendingNotificationAmount,
                token);
        }
        catch (TaskCanceledException)
        {
        }
    }



    // ============================================================
    // КОЛЕСО ЕДИНИЦЫ
    // ============================================================

    private void OnNotificationUnitWheelScrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        pendingNotificationUnitIndex =
            GetNotificationWheelIndex(
                e.ScrollY,
                3);


        notificationUnitIndex =
            pendingNotificationUnitIndex;


        UpdateNotificationWheelVisuals();


        if (!isNotificationWheelProgrammaticScroll)
        {
            notificationUnitSnapCancellation?.Cancel();

            notificationUnitSnapCancellation =
                new CancellationTokenSource();

            _ = SnapNotificationUnitAsync(
                notificationUnitSnapCancellation.Token);
        }
    }


    private async Task SnapNotificationUnitAsync(
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

            await SnapNotificationWheelAsync(
                NotificationUnitWheel,
                pendingNotificationUnitIndex,
                token);
        }
        catch (TaskCanceledException)
        {
        }
    }



    // ============================================================
    // ОБЩИЕ МЕТОДЫ КОЛЁС ОПОВЕЩЕНИЙ
    // ============================================================

    private static int GetNotificationWheelIndex(
        double scrollY,
        int count)
    {
        int index =
            (int)Math.Round(
                scrollY /
                NotificationWheelItemHeight);

        return Math.Clamp(
            index,
            0,
            count - 1);
    }


    private async Task SnapNotificationWheelAsync(
        ScrollView wheel,
        int index,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        double targetY =
            index *
            NotificationWheelItemHeight;


        isNotificationWheelProgrammaticScroll =
            true;


        try
        {
            await wheel.ScrollToAsync(
                0,
                targetY,
                true);
        }
        finally
        {
            isNotificationWheelProgrammaticScroll =
                false;
        }
    }


    private void UpdateNotificationWheelVisuals()
    {
        UpdateNotificationWheelVisuals(
            NotificationDirectionWheelLayout,
            pendingNotificationDirectionIndex);


        UpdateNotificationWheelVisuals(
            NotificationAmountWheelLayout,
            pendingNotificationAmount);


        UpdateNotificationWheelVisuals(
            NotificationUnitWheelLayout,
            pendingNotificationUnitIndex);
    }


    private static void UpdateNotificationWheelVisuals(
        VerticalStackLayout layout,
        int selectedIndex)
    {
        for (int i = 0;
             i < layout.Children.Count;
             i++)
        {
            if (layout.Children[i]
                is not Label label)
            {
                continue;
            }


            // Первый элемент — верхний spacer.
            int valueIndex =
                i - 1;


            if (valueIndex < 0)
            {
                continue;
            }


            double distance =
                Math.Abs(
                    valueIndex -
                    selectedIndex);


            if (valueIndex == selectedIndex)
            {
                label.FontSize =
                    38;

                label.FontAttributes =
                    FontAttributes.Bold;

                label.TextColor =
                    Colors.White;

                label.Opacity =
                    1.0;
            }
            else if (distance == 1)
            {
                label.FontSize =
                    34;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#737C86");

                label.Opacity =
                    0.9;
            }
            else if (distance == 2)
            {
                label.FontSize =
                    30;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#525A63");

                label.Opacity =
                    0.65;
            }
            else
            {
                label.FontSize =
                    28;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#414850");

                label.Opacity =
                    0.35;
            }
        }
    }



    // ============================================================
    // СОЗДАНИЕ КОЛЁС ОПОВЕЩЕНИЙ
    // ============================================================

    private void InitializeNotificationWheels()
    {
        UpdateNotificationDirectionWheel();

        // Количество: 00..99 (100 значений).
        // Индекс в колесе == отображаемое значение.
        CreateNotificationWheel(
            NotificationAmountWheelLayout,
            Enumerable.Range(0, 100)
                .Select(x => x.ToString("00"))
                .ToArray());

        CreateNotificationWheel(
            NotificationUnitWheelLayout,
            new[]
            {
                "Мин",
                "Час",
                "Ден",
            });

        pendingNotificationDirectionIndex = 0;

        // По умолчанию показываем "01".
        pendingNotificationAmount = 1;

        pendingNotificationUnitIndex = 1;

        UpdateNotificationWheelVisuals();
    }

    private void UpdateNotificationDirectionWheel()
    {
        NotificationDirectionWheelLayout.Children.Clear();

        bool hasDisplayPeriod =
            displayStart is not null ||
            displayEnd is not null;

        if (hasDisplayPeriod)
        {
            CreateNotificationWheel(
                NotificationDirectionWheelLayout,
                new[]
                {
                    "Чер",
                    "За",
                });
        }
        else
        {
            CreateNotificationWheel(
                NotificationDirectionWheelLayout,
                new[]
                {
                    "Чер",
                });

            // Если периода нет, "За" физически недоступно.
            notificationDirectionIndex = 0;
            pendingNotificationDirectionIndex = 0;
        }
    }


    private static void CreateNotificationWheel(
        VerticalStackLayout layout,
        IReadOnlyList<string> values)
    {
        // Верхний spacer.
        // Высота 90 подходит для колеса 240 px.
        layout.Children.Add(
            new BoxView
            {
                HeightRequest =
                    NotificationWheelTopPadding,

                InputTransparent =
                    true
            });


        foreach (string value in values)
        {
            Label label =
                new()
                {
                    Text =
                        value,

                    HeightRequest =
                        NotificationWheelItemHeight,

                    FontSize =
                        34,

                    HorizontalTextAlignment =
                        TextAlignment.Center,

                    VerticalTextAlignment =
                        TextAlignment.Center,

                    TextColor =
                        Color.FromArgb("#646D77"),

                    InputTransparent =
                        true
                };


            layout.Children.Add(
                label);
        }


        // Нижний spacer.
        layout.Children.Add(
            new BoxView
            {
                HeightRequest =
                    NotificationWheelTopPadding,

                InputTransparent =
                    true
            });
    }



    // ============================================================
    // ДОБАВЛЕНИЕ / УДАЛЕНИЕ ОПОВЕЩЕНИЙ
    // ============================================================

    private void AddNotificationTime(
        NotificationTimeItem source,
        DateTime notificationTime)
    {
        if (notificationTimes.Any(
                x => x.Time == notificationTime))
        {
            return;
        }

        // Создаём новый элемент на основе исходного,
        // но с новой датой/временем.
        NotificationTimeSettings settings =
            source.ToSettings();

        settings.Time =
            notificationTime;

        NotificationTimeItem item =
            new(settings);

        item.PropertyChanged +=
            OnNotificationTimeItemChanged;

        notificationTimes.Add(
            item);

        SortNotifications();

        RequestAutoSave();
    }


    private void OnDeleteNotificationClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is Button button &&
            button.CommandParameter
                is NotificationTimeItem item)
        {
            item.PropertyChanged -=
                OnNotificationTimeItemChanged;

            notificationTimes.Remove(
                item);

            RequestAutoSave();
        }
    }

    private void OnDuplicateNotificationClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is Button button &&
            button.CommandParameter
                is NotificationTimeItem item)
        {
            AddNotificationTime(
                item,
                item.Time.AddHours(1));
        }
    }


    private void OnNotificationTimeTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (sender is Label label &&
            label.BindingContext
                is NotificationTimeItem item)
        {
            editingNotification =
                item;

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


        foreach (NotificationTimeItem item
                 in sorted)
        {
            notificationTimes.Add(
                item);
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

        autoCompleteOnDisplayEnd =
            e.Value;

        RequestAutoSave();
    }


    private void OnShowInNotificationCenterChanged(
        object? sender,
        CheckedChangedEventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        showInNotificationCenter =
            e.Value;

        RequestAutoSave();
    }


    private void UpdateAutoCompleteControls()
    {
        bool hasDisplayEnd =
            displayEnd is not null;

        bool hasDisplayStart =
            displayStart is not null;

        bool hasDisplayPeriod =
            hasDisplayEnd ||
            hasDisplayStart;

        // "Завершить" показывается только при наличии конца периода.
        AutoCompleteCheckBox.IsVisible =
            hasDisplayEnd;

        AutoCompleteLabel.IsVisible =
            hasDisplayEnd;

        ShowInNotificationCenterCheckBox.IsVisible =
            !hasDisplayPeriod;

        ShowInNotificationCenterLabel.IsVisible =
            !hasDisplayPeriod;

        DisplayPeriodGrid.IsVisible =
            true;

        if (hasDisplayPeriod)
        {
            showInNotificationCenter =
                true;
        }

        if (!hasDisplayEnd)
        {
            autoCompleteOnDisplayEnd =
                false;
        }

        AutoCompleteCheckBox.IsChecked =
            hasDisplayEnd &&
            autoCompleteOnDisplayEnd;

        ShowInNotificationCenterCheckBox.IsChecked =
            showInNotificationCenter;
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

        UpdateTimerFromCurrentTarget();


        if (!countdownTimer.IsRunning)
        {
            countdownTimer.Start();
        }
    }


    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        countdownTimer.Stop();


        autoSaveCancellation?.Cancel();


        daysSnapCancellation?.Cancel();

        hoursSnapCancellation?.Cancel();

        minutesSnapCancellation?.Cancel();


        notificationDirectionSnapCancellation?.Cancel();

        notificationAmountSnapCancellation?.Cancel();

        notificationUnitSnapCancellation?.Cancel();
    }


    protected override bool OnBackButtonPressed()
    {
        _ = SaveAndCloseAsync(
            ReminderTextEditor.Text?.Trim()
            ?? string.Empty);

        return true;
    }


    /// <summary>
    /// Сохраняет напоминание (через SaveRequested — MainPage обновляет
    /// список, хранилище и notification center) и закрывает страницу,
    /// возвращая пользователя на MainPage.
    /// </summary>
    private async Task SaveAndCloseAsync(
        string text)
    {
        if (isClosing || isSaving)
        {
            return;
        }

        isSaving = true;

        // ------------------------------------------------------------
        // 1. Сохранение
        // ------------------------------------------------------------

        try
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                await NotifySaveRequestedAsync(
                    CreateReminderForSave(text));
            }
        }
        catch (Exception ex)
        {
            isSaving = false;

            System.Diagnostics.Debug.WriteLine(
                $"Ошибка сохранения ReminderEditorPage: {ex}");

            await DisplayAlert(
                "Ошибка",
                $"Не удалось сохранить напоминание.\n\n{ex.GetType().Name}: {ex.Message}",
                "OK");

            return;
        }

        // ------------------------------------------------------------
        // 2. Возврат на MainPage
        // ------------------------------------------------------------

        isClosing = true;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(
                async () =>
                {
                    await Navigation.PopModalAsync();
                });
        }
        catch (Exception ex)
        {
            isClosing = false;
            isSaving = false;

            System.Diagnostics.Debug.WriteLine(
                $"Ошибка закрытия ReminderEditorPage: {ex}");

            await DisplayAlert(
                "Ошибка",
                $"Не удалось закрыть страницу.\n\n{ex.GetType().Name}: {ex.Message}",
                "OK");
        }
    }



    // ============================================================
    // БЫСТРЫЕ КНОПКИ ДАТЫ / ВРЕМЕНИ
    // ============================================================

    private void settime0300(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(3, 0, 0);
    }

    private void settime0600(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(6, 0, 0);
    }

    private void settime0900(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(9, 0, 0);
    }

    private void settime1200(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(12, 0, 0);
    }

    private void settime1500(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(15, 0, 0);
    }

    private void settime1800(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(18, 0, 0);
    }


    private void settime2100(
        object? sender,
        EventArgs e)
    {
        OverlayTimePicker.Time =
            new TimeSpan(21, 0, 0);
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

    private DateTime? GetCurrentTimerTarget()
    {
        if (DateTimePickerOverlay.IsVisible)
        {
            return
                OverlayDatePicker.Date +
                OverlayTimePicker.Time;
        }


        if (editingNotification is not null)
        {
            return editingNotification.Time;
        }


        return selectedBoundary ==
               DisplayBoundary.Start

            ? displayStart

            : displayEnd;
    }


    private DateTime? GetSelectedBoundaryDateTime()
    {
        return selectedBoundary ==
               DisplayBoundary.Start

            ? displayStart

            : displayEnd;
    }

    // ============================================================
    // ОТКРЫТИЕ ТАЙМЕРА КНОПКОЙ 🔁
    // ============================================================

    private async void OnRepeatTimerClicked(
    object? sender,
    EventArgs e)
    {
        // Длительность можно сохранять только для оповещения.
        if (editingNotification is null)
        {
            return;
        }

        isSavingNotificationTimerDuration = true;

        // Если длительность уже сохранена — показываем её.
        if (editingNotification.TimerDuration is TimeSpan saved)
        {
            pendingTimerDays = Math.Clamp(saved.Days, 0, 99);
            pendingTimerHours = Math.Clamp(saved.Hours, 0, 23);
            pendingTimerMinutes = Math.Clamp(saved.Minutes, 0, 59);
        }
        else
        {
            // По умолчанию для повтора — 1 день.
            pendingTimerDays = 1;
            pendingTimerHours = 0;
            pendingTimerMinutes = 0;
        }

        await ShowTimerDurationOverlayAsync();
    }


    // ============================================================
    // ОТОБРАЖЕНИЕ ОКНА И ПОЗИЦИОНИРОВАНИЕ КОЛЁС
    // ============================================================

    private async Task ShowTimerDurationOverlayAsync()
    {
        TimerDurationOverlay.IsVisible = true;

        await Task.Delay(50);

        isTimerWheelProgrammaticScroll = true;

        try
        {
            await DaysWheel.ScrollToAsync(
                0,
                pendingTimerDays * TimerWheelItemHeight,
                false);

            await HoursWheel.ScrollToAsync(
                0,
                pendingTimerHours * TimerWheelItemHeight,
                false);

            await MinutesWheel.ScrollToAsync(
                0,
                pendingTimerMinutes * TimerWheelItemHeight,
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


    private void UpdateTimerFromCurrentTarget()
    {
        DateTime? target =
            GetCurrentTimerTarget();


        if (target is null)
        {
            SetTimerDisplay(
                TimeSpan.Zero);

            return;
        }


        UpdateTimerDisplay(
            target.Value -
            DateTime.Now);
    }


    private void UpdateTimerFromDateTimePicker()
    {
        DateTime selectedDateTime =
            OverlayDatePicker.Date +
            OverlayTimePicker.Time;


        UpdateTimerDisplay(
            selectedDateTime -
            DateTime.Now);
    }


    private void UpdateTimerFromSelectedBoundary()
    {
        DateTime? target =
            GetSelectedBoundaryDateTime();


        if (target is null)
        {
            SetTimerDisplay(
                TimeSpan.Zero);

            return;
        }


        UpdateTimerDisplay(
            target.Value -
            DateTime.Now);
    }



    // ============================================================
    // СОЗДАНИЕ КОЛЁС ТАЙМЕРА
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
        layout.Children.Add(
            new BoxView
            {
                HeightRequest =
                    TimerWheelTopPadding,

                InputTransparent =
                    true
            });


        for (int i = 0;
             i < count;
             i++)
        {
            Label label =
                new()
                {
                    Text =
                        i.ToString("00"),

                    HeightRequest =
                        TimerWheelItemHeight,

                    FontSize =
                        34,

                    HorizontalTextAlignment =
                        TextAlignment.Center,

                    VerticalTextAlignment =
                        TextAlignment.Center,

                    TextColor =
                        Color.FromArgb("#646D77"),

                    InputTransparent =
                        true
                };


            layout.Children.Add(
                label);
        }


        layout.Children.Add(
            new BoxView
            {
                HeightRequest =
                    TimerWheelTopPadding,

                InputTransparent =
                    true
            });
    }


    private static int GetSelectedWheelIndex(
        double scrollY,
        int count)
    {
        int index =
            (int)Math.Round(
                scrollY /
                TimerWheelItemHeight);


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
            if (layout.Children[i]
                is not Label label)
            {
                continue;
            }


            int valueIndex =
                i - 1;


            if (valueIndex < 0)
            {
                continue;
            }


            double distance =
                Math.Abs(
                    valueIndex -
                    selectedIndex);


            if (valueIndex == selectedIndex)
            {
                label.FontSize =
                    38;

                label.FontAttributes =
                    FontAttributes.Bold;

                label.TextColor =
                    Colors.White;

                label.Opacity =
                    1.0;
            }
            else if (distance == 1)
            {
                label.FontSize =
                    34;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#737C86");

                label.Opacity =
                    0.9;
            }
            else if (distance == 2)
            {
                label.FontSize =
                    30;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#525A63");

                label.Opacity =
                    0.65;
            }
            else
            {
                label.FontSize =
                    28;

                label.FontAttributes =
                    FontAttributes.None;

                label.TextColor =
                    Color.FromArgb("#414850");

                label.Opacity =
                    0.35;
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
    // ОБЩИЙ SNAP ТАЙМЕРА
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
            index *
            TimerWheelItemHeight;


        isTimerWheelProgrammaticScroll =
            true;


        try
        {
            await wheel.ScrollToAsync(
                0,
                targetY,
                true);
        }
        finally
        {
            isTimerWheelProgrammaticScroll =
                false;
        }
    }



    // ============================================================
    // ОТКРЫТИЕ ВЫБОРА ТАЙМЕРА
    // ============================================================

    private async void OnTimerDisplayTapped(
        object? sender,
        TappedEventArgs e)
    {
        // Обычный режим: редактирование даты/времени
        // через длительность таймера.
        isSavingNotificationTimerDuration = false;

        UpdateTimerFromCurrentTarget();

        pendingTimerDays = Math.Clamp(timerDays, 0, 99);
        pendingTimerHours = Math.Clamp(timerHours, 0, 23);
        pendingTimerMinutes = Math.Clamp(timerMinutes, 0, 59);

        await ShowTimerDurationOverlayAsync();
    }



    // ============================================================
    // СОХРАНЕНИЕ ТАЙМЕРА
    // ============================================================

    private void OnSaveTimerClicked(
    object? sender,
    EventArgs e)
    {
        TimeSpan duration =
            new(
                pendingTimerDays,
                pendingTimerHours,
                pendingTimerMinutes,
                0);

        // ========================================================
        // СОХРАНЕНИЕ ДЛИТЕЛЬНОСТИ ПО КНОПКЕ 🔁
        // ========================================================

        if (isSavingNotificationTimerDuration)
        {
            if (editingNotification is not null)
            {
                // 0 дн + 0 ч + 0 мин => повтор не задан (null, а не 00:00:00).
                editingNotification.TimerDuration =
                    duration == TimeSpan.Zero
                        ? null
                        : duration;

                RequestAutoSave();
            }

            isSavingNotificationTimerDuration = false;

            // Закрываем окно после подтверждения.
            TimerDurationOverlay.IsVisible = false;

            return;
        }

        // ========================================================
        // СТАРОЕ ПОВЕДЕНИЕ ДЛЯ ОБЫЧНОГО ТАЙМЕРА
        // ========================================================

        DateTime targetDateTime =
            GetCurrentMinute().Add(duration);


        // ========================================================
        // NotificationTimeItem
        // ========================================================

        if (editingNotification is not null)
        {
            editingNotification.Time =
                targetDateTime;


            SortNotifications();


            NotificationTimesCollectionView.ItemsSource =
                null;


            NotificationTimesCollectionView.ItemsSource =
                notificationTimes;


            isUpdatingPickers =
                true;


            OverlayDatePicker.Date =
                targetDateTime.Date;


            OverlayTimePicker.Time =
                targetDateTime.TimeOfDay;


            isUpdatingPickers =
                false;


            UpdateSelectedDateTimeLabels();

            UpdateDisplayPeriodLabel();

            UpdateAutoCompleteControls();

            UpdateNotificationReferenceButtons();

            UpdateNotificationDirectionWheel();

            UpdateTimerFromCurrentTarget();

            RequestAutoSave();


            TimerDurationOverlay.IsVisible =
                false;


            return;
        }


        // ========================================================
        // Start / End
        // ========================================================

        if (selectedBoundary ==
            DisplayBoundary.Start)
        {
            displayStart =
                targetDateTime;
        }
        else
        {
            displayEnd =
                targetDateTime;
        }


        isUpdatingPickers =
            true;


        OverlayDatePicker.Date =
            targetDateTime.Date;


        OverlayTimePicker.Time =
            targetDateTime.TimeOfDay;


        isUpdatingPickers =
            false;


        UpdateSelectedDateTimeLabels();

        UpdateDisplayPeriodLabel();

        UpdateAutoCompleteControls();

        UpdateNotificationReferenceButtons();

        UpdateTimerFromCurrentTarget();

        RequestAutoSave();


        TimerDurationOverlay.IsVisible =
            false;
    }



    // ============================================================
    // ОБНОВЛЕНИЕ ТАЙМЕРА
    // ============================================================

    private void OnCountdownTimerTick(
        object? sender,
        EventArgs e)
    {
        if (DateTimePickerOverlay.IsVisible)
        {
            UpdateTimerFromDateTimePicker();

            return;
        }


        UpdateTimerFromCurrentTarget();
    }


    private void UpdateTimerDisplay(
        TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            TimeSpan elapsed =
                remaining.Duration();

            TimeSpan roundedElapsed =
                TimeSpan.FromMinutes(
                    Math.Floor(
                        elapsed.TotalMinutes));

            SetTimerDisplay(
                roundedElapsed,
                true);

            return;
        }


        TimeSpan roundedRemaining =
            TimeSpan.FromMinutes(
                Math.Ceiling(
                    remaining.TotalMinutes));


        SetTimerDisplay(
            roundedRemaining,
            false);
    }



    // ============================================================
    // СБРОС START / END
    // ============================================================

    private void OnResetDateTimeClicked(
        object? sender,
        EventArgs e)
    {
        // --------------------------------------------------------
        // Сброс начала
        // --------------------------------------------------------

        if (selectedBoundary ==
            DisplayBoundary.Start)
        {
            displayStart =
                null;


            DateTime initialDate;
            TimeSpan initialTime;


            if (displayEnd is DateTime savedEnd)
            {
                initialDate =
                    savedEnd.Date.AddDays(-1);

                initialTime =
                    TimeSpan.Zero;
            }
            else
            {
                initialDate =
                    DateTime.Today;

                initialTime =
                    TimeSpan.Zero;
            }


            isUpdatingPickers =
                true;


            OverlayDatePicker.Date =
                initialDate;


            OverlayTimePicker.Time =
                initialTime;


            isUpdatingPickers =
                false;
        }


        // --------------------------------------------------------
        // Сброс конца
        // --------------------------------------------------------

        else
        {
            displayEnd =
                null;


            DateTime initialDate;
            TimeSpan initialTime;


            if (displayStart is DateTime savedStart)
            {
                initialDate =
                    savedStart.Date.AddDays(1);

                initialTime =
                    new TimeSpan(23, 0, 0);
            }
            else
            {
                initialDate =
                    DateTime.Today.AddDays(1);

                initialTime =
                    new TimeSpan(23, 0, 0);
            }


            isUpdatingPickers =
                true;


            OverlayDatePicker.Date =
                initialDate;


            OverlayTimePicker.Time =
                initialTime;


            isUpdatingPickers =
                false;
        }


        UpdateSelectedDateTimeLabels();

        UpdateDisplayPeriodLabel();

        UpdateAutoCompleteControls();

        UpdateNotificationReferenceButtons();

        UpdateNotificationDirectionWheel();

        UpdateTimerFromDateTimePicker();

        RequestAutoSave();
    }


    private static DateTime GetCurrentMinute()
    {
        DateTime now =
            DateTime.Now;


        return new DateTime(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            0,
            now.Kind);
    }


    private void SetTimerDisplay(
        TimeSpan duration,
        bool isExpired = false)
    {
        timerDays =
            duration.Days;

        timerHours =
            duration.Hours;

        timerMinutes =
            duration.Minutes;


        TimerDaysDisplayLabel.Text =
            timerDays.ToString("00");

        TimerHoursDisplayLabel.Text =
            timerHours.ToString("00");

        TimerMinutesDisplayLabel.Text =
            timerMinutes.ToString("00");


        TimerElapsedSignLabel.IsVisible =
            isExpired;


        Color normalColor =
            Application.Current?.RequestedTheme ==
            AppTheme.Dark

                ? TimerNormalDarkColor

                : TimerNormalLightColor;


        Color displayColor =
            isExpired
                ? TimerExpiredColor
                : normalColor;


        TimerDaysDisplayLabel.TextColor =
            displayColor;

        TimerHoursDisplayLabel.TextColor =
            displayColor;

        TimerMinutesDisplayLabel.TextColor =
            displayColor;

        TimerElapsedSignLabel.TextColor =
            displayColor;
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

    // ============================================================
    // ОТМЕНА ТАЙМЕРА
    // ============================================================

    private async void OnCancelTimerClicked(
    object? sender,
    EventArgs e)
    {
        // Отменяем ожидающий snap,
        // чтобы он не изменил колёса после закрытия окна.
        daysSnapCancellation?.Cancel();
        hoursSnapCancellation?.Cancel();
        minutesSnapCancellation?.Cancel();

        // --------------------------------------------------------
        // Восстанавливаем исходные значения колёс.
        //
        // timerDays / timerHours / timerMinutes —
        // это последнее ПОДТВЕРЖДЁННОЕ значение.
        //
        // pendingTimer* — временные значения, изменяемые
        // пользователем во время открытого окна.
        // --------------------------------------------------------

        pendingTimerDays =
            timerDays;

        pendingTimerHours =
            timerHours;

        pendingTimerMinutes =
            timerMinutes;

        // --------------------------------------------------------
        // Возвращаем визуальное положение колёс
        // к подтверждённому значению.
        // --------------------------------------------------------

        isTimerWheelProgrammaticScroll =
            true;

        try
        {
            await DaysWheel.ScrollToAsync(
                0,
                timerDays * TimerWheelItemHeight,
                false);

            await HoursWheel.ScrollToAsync(
                0,
                timerHours * TimerWheelItemHeight,
                false);

            await MinutesWheel.ScrollToAsync(
                0,
                timerMinutes * TimerWheelItemHeight,
                false);
        }
        finally
        {
            isTimerWheelProgrammaticScroll =
                false;
        }

        // Обновляем оформление выбранных элементов.
        UpdateWheelVisuals(
            DaysWheelLayout,
            timerDays);

        UpdateWheelVisuals(
            HoursWheelLayout,
            timerHours);

        UpdateWheelVisuals(
            MinutesWheelLayout,
            timerMinutes);

        // Сбрасываем режим сохранения.
        // TimerDuration в NotificationTimeItem не изменяется.
        isSavingNotificationTimerDuration = false;

        // Закрываем окно без применения изменений.
        TimerDurationOverlay.IsVisible = false;
    }
}