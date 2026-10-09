using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace Reminder;

public partial class MainPage : ContentPage
{

    private readonly ObservableCollection<ReminderItem> reminders;
    private readonly ObservableCollection<ReminderItem> completedReminders;
    private readonly ReminderStore store;
    private readonly IReminderNotificationService notificationService;
    private readonly SemaphoreSlim editorNavigationSemaphore = new(1, 1);

    private ReminderItem? groupSelectionReminder;

    private int? openEditorReminderId;
    private CancellationTokenSource? groupSortCancellation;
    private bool isSortingReminders;

    public MainPage()
    {
        InitializeComponent();

        store = new ReminderStore();

        notificationService =
            IPlatformApplication.Current?.Services
                .GetRequiredService<IReminderNotificationService>()
            ?? throw new InvalidOperationException(
                "Notification service is not registered.");

        reminders = new ObservableCollection<ReminderItem>(
            ReminderOrdering.Order(store.Load()));

        SubscribeToReminderChanges();

        completedReminders =
            new ObservableCollection<ReminderItem>(
                store.LoadCompleted());

        RemindersCollectionView.ItemsSource = reminders;

        SubscribeToNotificationCompletion();
    }

    private bool isReminderSortScheduled;

    private void ScheduleRemindersSort()
    {
        if (isSortingReminders ||
            isReminderSortScheduled)
        {
            return;
        }

        isReminderSortScheduled = true;

        _ = SortRemindersAfterCollectionChangeAsync();
    }


    private async Task SortRemindersAfterCollectionChangeAsync()
    {
        try
        {
            // Дожидаемся завершения текущего события
            // CollectionChanged перед изменением коллекции.
            await Task.Yield();

            await MainThread.InvokeOnMainThreadAsync(
                SortReminders);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Не удалось отсортировать напоминания: {ex}");
        }
        finally
        {
            isReminderSortScheduled = false;
        }
    }


    // ============================================================
    // ЕДИНАЯ ТОЧКА СИНХРОНИЗАЦИИ С NOTIFICATION CENTER
    // ============================================================

    private void SyncNotificationCenter()
    {
        try
        {
            notificationService.SyncNotificationCenter(
                ReminderOrdering.Order(reminders).ToList());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Не удалось обновить notification center: {ex}");
        }
    }

    private async Task RescheduleAllAlarmsAsync()
    {
        foreach (ReminderItem reminder in reminders.ToList())
        {
            try
            {
                await notificationService.ScheduleAsync(reminder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Не удалось запланировать напоминание {reminder.Id}: {ex}");
            }
        }
    }


    // ============================================================
    // ГРУППЫ
    // ============================================================

    private void ScheduleGroupSort()
    {
        groupSortCancellation?.Cancel();
        groupSortCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        groupSortCancellation = cancellation;

        _ = SortAfterGroupChangeAsync(cancellation);
    }

    private void OnGroupSelectionOverlayTapped(
        object? sender,
        TappedEventArgs e)
    {
        GroupSelectionOverlay.IsVisible = false;
        groupSelectionReminder = null;
    }

    private async Task SortAfterGroupChangeAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(3),
                cancellation.Token);

            if (!cancellation.IsCancellationRequested)
            {
                await MainThread.InvokeOnMainThreadAsync(
                    SortReminders);
            }
        }
        catch (OperationCanceledException)
        {
            // Таймер был сброшен новым изменением группы.
        }
        finally
        {
            if (ReferenceEquals(
                groupSortCancellation,
                cancellation))
            {
                groupSortCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void OnGroupSelectionButtonClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.CommandParameter is not ReminderItem reminder)
        {
            return;
        }

        groupSelectionReminder = reminder;
        GroupSelectionOverlay.IsVisible = true;
    }

    private void OnGroupButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (groupSelectionReminder is null)
        {
            return;
        }

        if (sender is not Button button)
        {
            return;
        }

        if (!int.TryParse(
                button.CommandParameter?.ToString(),
                out int group))
        {
            return;
        }

        if (group < 1 || group > 4)
        {
            return;
        }

        groupSelectionReminder.Group = group;

        SaveReminders();

        // Список на экране переставится через 3 секунды (ScheduleGroupSort),
        // а шторка пересобирается сразу: её порядок считается из данных.
        SyncNotificationCenter();

        GroupSelectionOverlay.IsVisible = false;
        groupSelectionReminder = null;
    }


    // ============================================================
    // ЖИЗНЕННЫЙ ЦИКЛ
    // ============================================================

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            ReloadReminders();

            // Как и раньше: при открытии приложения убираем оверлеи/будильники.
            DismissVisibleNotifications();

            CompleteExpiredAutoCompleteReminders();

            await RescheduleAllAlarmsAsync();

            SyncNotificationCenter();

            await TryOpenPendingReminderEditorAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка OnAppearing: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        groupSortCancellation?.Cancel();

        GroupSelectionOverlay.IsVisible = false;
        groupSelectionReminder = null;

        base.OnDisappearing();
    }


    // ============================================================
    // СОЗДАНИЕ / РЕДАКТИРОВАНИЕ
    // ============================================================

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var editorPage = new ReminderEditorPage();

        ReminderItem? created = null;

        editorPage.SaveRequested += async editedReminder =>
        {
            created = await SaveEditedReminderAsync(
                created,
                editedReminder);
        };

        await Navigation.PushModalAsync(
            new NavigationPage(editorPage));
    }

    private async Task OpenEditorAsync(ReminderItem reminder)
    {
        await editorNavigationSemaphore.WaitAsync();

        try
        {
            if (openEditorReminderId is not null)
            {
                return;
            }

            openEditorReminderId = reminder.Id;
        }
        finally
        {
            editorNavigationSemaphore.Release();
        }

        var editorPage = new ReminderEditorPage(reminder);

        editorPage.SaveRequested += async editedReminder =>
        {
            await SaveEditedReminderAsync(
                reminder,
                editedReminder);
        };

        editorPage.DeleteRequested +=
            (_, _) => CompleteReminder(reminder.Id);

        editorPage.Disappearing +=
            (_, _) => openEditorReminderId = null;

        try
        {
            await Navigation.PushModalAsync(
                new NavigationPage(editorPage));
        }
        catch
        {
            openEditorReminderId = null;
            throw;
        }
    }

    /// <summary>
    /// Создание и редактирование проходят одним путём:
    /// данные → хранилище → будильники → шторка.
    /// </summary>
    private async Task<ReminderItem> SaveEditedReminderAsync(
        ReminderItem? target,
        ReminderItem edited)
    {
        if (target is null)
        {
            target = new ReminderItem
            {
                Id = GetNextReminderId()
            };

            ApplyEditedFields(target, edited);

            reminders.Add(target);
        }
        else
        {
            // Старые будильники описаны в хранилище, поэтому отменяем
            // их ДО записи новых данных.
            notificationService.Cancel(target.Id);

            ApplyEditedFields(target, edited);
        }

        RefreshReminders();
        SaveReminders();

        try
        {
            await notificationService.ScheduleAsync(target);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Не удалось запланировать напоминание {target.Id}: {ex}");
        }

        SyncNotificationCenter();

        return target;
    }

    private static void ApplyEditedFields(
        ReminderItem target,
        ReminderItem edited)
    {
        target.Text = edited.Text;
        target.DisplayStart = edited.DisplayStart;
        target.DisplayEnd = edited.DisplayEnd;
        target.Group = edited.Group;
        target.AutoCompleteOnDisplayEnd =
            edited.AutoCompleteOnDisplayEnd;
        target.ShowInNotificationCenter =
            edited.ShowInNotificationCenter;
        target.NotificationTimes =
            edited.NotificationTimes;
        target.NotificationTimeSettings =
            edited.NotificationTimeSettings;
    }

    private async void OnCompletedClicked(
        object? sender,
        EventArgs e)
    {
        await Navigation.PushModalAsync(
            new NavigationPage(
                new CompletedRemindersPage()));
    }

    private async void OnReminderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ReminderItem reminder)
        {
            return;
        }

        await OpenEditorAsync(reminder);
    }


    // ============================================================
    // ЗАВЕРШЕНИЕ
    // ============================================================

    private void OnCompleteReminderClicked(
        object? sender,
        EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is ReminderItem reminder)
        {
            CompleteReminder(reminder.Id);
        }
    }

    /// <summary>Завершить + сохранить оба списка + обновить шторку.</summary>
    private void CompleteReminder(int reminderId)
    {
        CompleteReminderCore(reminderId);

        SaveReminders();
        SaveCompletedReminders();

        SyncNotificationCenter();
    }

    /// <summary>Только изменение коллекций и отмена будильников.</summary>
    private void CompleteReminderCore(int reminderId)
    {
        ReminderItem? reminder =
            reminders.FirstOrDefault(
                item => item.Id == reminderId);

        if (reminder is not null)
        {
            reminders.Remove(reminder);

            reminder.CompletedAt = DateTime.Now;
            reminder.NotificationTimes.Clear();
            reminder.NotificationTimeSettings.Clear();

            completedReminders.Add(reminder);
        }

        // Будильники ещё описаны в хранилище, поэтому отменяем
        // до SaveReminders().
        notificationService.Cancel(reminderId);
    }

    private void CompleteExpiredAutoCompleteReminders()
    {
        DateTime now = DateTime.Now;

        List<int> expiredReminderIds = reminders
            .Where(reminder =>
                reminder.AutoCompleteOnDisplayEnd &&
                reminder.DisplayEnd is DateTime displayEnd &&
                displayEnd <= now)
            .Select(static reminder => reminder.Id)
            .ToList();

        if (expiredReminderIds.Count == 0)
        {
            return;
        }

        foreach (int reminderId in expiredReminderIds)
        {
            CompleteReminderCore(reminderId);
        }

        SaveReminders();
        SaveCompletedReminders();

        // Шторку пересобирает вызывающий код (OnAppearing).
    }


    // ============================================================
    // СПИСКИ
    // ============================================================

    private int GetNextReminderId()
    {
        return reminders
            .Concat(completedReminders)
            .Any()
            ? reminders
                .Concat(completedReminders)
                .Max(static reminder => reminder.Id) + 1
            : 1;
    }

    private void RefreshReminders()
    {
        SortReminders();

        RemindersCollectionView.ItemsSource = null;
        RemindersCollectionView.ItemsSource = reminders;
    }

    private void ReloadReminders()
    {
        UnsubscribeFromReminderChanges();

        try
        {
            isSortingReminders = true;

            reminders.Clear();

            foreach (ReminderItem reminder in store.Load())
            {
                reminders.Add(reminder);
            }
        }
        finally
        {
            isSortingReminders = false;
            SubscribeToReminderChanges();
        }

        SortReminders();

        completedReminders.Clear();

        foreach (ReminderItem reminder in store.LoadCompleted())
        {
            completedReminders.Add(reminder);
        }
    }

    private async Task TryOpenPendingReminderEditorAsync()
    {
#if ANDROID
        int? reminderId =
            AndroidReminderNotificationService
                .ConsumePendingReminderEditorRequest();

        if (reminderId is not int id ||
            id == 0)
        {
            return;
        }

        ReloadReminders();

        ReminderItem? reminder =
            reminders.FirstOrDefault(
                item => item.Id == id);

        if (reminder is null)
        {
            return;
        }

        await OpenEditorAsync(reminder);
#endif
    }

    private void SubscribeToNotificationCompletion()
    {
#if ANDROID
        // "Завершить" из шторки/оверлея: ресивер уже обновил хранилище
        // и шторку. Здесь только перечитываем оба списка из хранилища.
        AndroidReminderNotificationService.ReminderCompleted +=
            _ => MainThread.BeginInvokeOnMainThread(ReloadReminders);

        AndroidReminderNotificationService.ReminderEditorRequested +=
            reminderId =>
            {
                MainThread.BeginInvokeOnMainThread(
                    async () =>
                    {
                        ReloadReminders();

                        ReminderItem? reminder =
                            reminders.FirstOrDefault(
                                item => item.Id == reminderId);

                        if (reminder is not null)
                        {
                            await OpenEditorAsync(reminder);
                        }
                    });
            };

        AndroidReminderNotificationService.NotificationTimeTriggered +=
            (reminderId, notificationTime) =>
            {
                MainThread.BeginInvokeOnMainThread(
                    () => RemoveTriggeredNotificationTime(
                        reminderId,
                        notificationTime));
            };

        AndroidReminderNotificationService.NotificationTimeDeferred +=
            _ =>
            {
                MainThread.BeginInvokeOnMainThread(
                    ReloadReminders);
            };
#endif
    }

    private void RemoveTriggeredNotificationTime(
        int reminderId,
        DateTime notificationTime)
    {
        ReminderItem? reminder =
            reminders.FirstOrDefault(
                item => item.Id == reminderId);

        if (reminder is null)
        {
            ReloadReminders();
            return;
        }

        if (reminder.NotificationTimes.RemoveAll(
                time => time == notificationTime) > 0)
        {
            reminder.NotificationTimeSettings.RemoveAll(
                time => time.Time == notificationTime);

            RefreshReminders();
        }
    }

    private void DismissVisibleNotifications()
    {
        foreach (ReminderItem reminder in reminders)
        {
            notificationService.Cancel(reminder.Id);
        }
    }

    private void SaveReminders()
    {
        store.Save(reminders);
    }

    private void SaveCompletedReminders()
    {
        store.SaveCompleted(completedReminders);
    }

    private void SortReminders()
    {
        List<ReminderItem> sortedReminders =
            ReminderOrdering.Order(reminders).ToList();

        if (reminders.SequenceEqual(sortedReminders))
        {
            return;
        }

        isSortingReminders = true;

        try
        {
            for (
                int targetIndex = 0;
                targetIndex < sortedReminders.Count;
                targetIndex++)
            {
                int currentIndex =
                    reminders.IndexOf(
                        sortedReminders[targetIndex]);

                if (currentIndex != targetIndex)
                {
                    reminders.Move(
                        currentIndex,
                        targetIndex);
                }
            }
        }
        finally
        {
            isSortingReminders = false;
        }
    }

    private void SubscribeToReminderChanges()
    {
        reminders.CollectionChanged +=
            OnRemindersCollectionChanged;

        foreach (ReminderItem reminder in reminders)
        {
            reminder.PropertyChanged +=
                OnReminderPropertyChanged;
        }
    }

    private void UnsubscribeFromReminderChanges()
    {
        reminders.CollectionChanged -=
            OnRemindersCollectionChanged;

        foreach (ReminderItem reminder in reminders)
        {
            reminder.PropertyChanged -=
                OnReminderPropertyChanged;
        }
    }

    private void OnRemindersCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        // Отписываемся от изменённых или удалённых элементов.
        if (e.OldItems is not null)
        {
            foreach (ReminderItem reminder in e.OldItems)
            {
                reminder.PropertyChanged -=
                    OnReminderPropertyChanged;
            }
        }

        // Подписываемся на новые элементы.
        if (e.NewItems is not null)
        {
            foreach (ReminderItem reminder in e.NewItems)
            {
                reminder.PropertyChanged +=
                    OnReminderPropertyChanged;
            }
        }

        // Не изменяем ObservableCollection непосредственно
        // внутри события CollectionChanged.
        if (!isSortingReminders)
        {
            ScheduleRemindersSort();
        }
    }

    private void OnReminderPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReminderItem.Group))
        {
            ScheduleGroupSort();
            return;
        }

        if (e.PropertyName is
            nameof(ReminderItem.DisplayStart) or
            nameof(ReminderItem.DisplayEnd))
        {
            SortReminders();
        }
    }
}
