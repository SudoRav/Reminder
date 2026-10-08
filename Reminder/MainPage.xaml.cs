using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

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
            OrderReminders(store.Load(), DateTime.Now));

        SubscribeToReminderChanges();

        completedReminders =
            new ObservableCollection<ReminderItem>(
                store.LoadCompleted());

        RemindersCollectionView.ItemsSource = reminders;

        SubscribeToNotificationCompletion();
    }


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

        GroupSelectionOverlay.IsVisible = false;
        groupSelectionReminder = null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ReloadReminders();
        DismissVisibleNotifications();

        CompleteExpiredAutoCompleteReminders();

        foreach (ReminderItem reminder in reminders)
        {
            await ShowOrCancelNotificationAsync(reminder);
        }

        await TryOpenPendingReminderEditorAsync();
    }

    protected override void OnDisappearing()
    {
        groupSortCancellation?.Cancel();

        GroupSelectionOverlay.IsVisible = false;
        groupSelectionReminder = null;

        base.OnDisappearing();
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var editorPage = new ReminderEditorPage();

        ReminderItem? reminder = null;

        editorPage.SaveRequested += async editedReminder =>
        {
            if (reminder is null)
            {
                reminder = new ReminderItem
                {
                    Id = GetNextReminderId()
                };

                reminders.Add(reminder);
            }
            else
            {
                notificationService.Cancel(reminder.Id);
            }

            reminder.Text = editedReminder.Text;
            reminder.DisplayStart = editedReminder.DisplayStart;
            reminder.DisplayEnd = editedReminder.DisplayEnd;
            reminder.Group = editedReminder.Group;
            reminder.AutoCompleteOnDisplayEnd =
                editedReminder.AutoCompleteOnDisplayEnd;
            reminder.ShowInNotificationCenter =
                editedReminder.ShowInNotificationCenter;
            reminder.NotificationTimes =
                editedReminder.NotificationTimes;
            reminder.NotificationTimeSettings =
                editedReminder.NotificationTimeSettings;

            // Group намеренно здесь не изменяется.
            // Для нового ReminderItem он остается равным 3.
            RefreshReminders();
            SaveReminders();

            await ShowOrCancelNotificationAsync(reminder);
        };

        await Navigation.PushModalAsync(
            new NavigationPage(editorPage));
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

    private void OnCompleteReminderClicked(
        object? sender,
        EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is ReminderItem reminder)
        {
            CompleteReminder(reminder);
        }
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
            notificationService.Cancel(reminder.Id);

            reminder.Text = editedReminder.Text;
            reminder.DisplayStart = editedReminder.DisplayStart;
            reminder.DisplayEnd = editedReminder.DisplayEnd;
            reminder.Group = editedReminder.Group;
            reminder.AutoCompleteOnDisplayEnd =
                editedReminder.AutoCompleteOnDisplayEnd;
            reminder.ShowInNotificationCenter =
                editedReminder.ShowInNotificationCenter;
            reminder.NotificationTimes =
                editedReminder.NotificationTimes;
            reminder.NotificationTimeSettings =
                editedReminder.NotificationTimeSettings;

            // Group намеренно здесь не изменяется.
            RefreshReminders();
            SaveReminders();

            await ShowOrCancelNotificationAsync(reminder);
        };

        editorPage.DeleteRequested +=
            (_, _) => CompleteReminder(reminder);

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

    private void CompleteReminder(ReminderItem reminder)
    {
        CompleteReminder(reminder.Id, saveReminders: true);
    }

    private void CompleteReminder(
        int reminderId,
        bool saveReminders)
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

        notificationService.Cancel(reminderId);

        if (saveReminders || reminder is not null)
        {
            SaveReminders();
            SaveCompletedReminders();
        }
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

        foreach (int reminderId in expiredReminderIds)
        {
            CompleteReminder(
                reminderId,
                saveReminders: true);
        }
    }

    private async Task ShowOrCancelNotificationAsync(ReminderItem reminder)
    {
        bool hasDisplayPeriod =
            reminder.DisplayStart is not null ||
            reminder.DisplayEnd is not null;

        bool shouldShowNotification =
            hasDisplayPeriod ||
            reminder.ShowInNotificationCenter;

        if (!shouldShowNotification)
        {
            notificationService.Cancel(reminder.Id);
            return;
        }

        await notificationService.ShowAsync(reminder);
    }

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
        AndroidReminderNotificationService.ReminderCompleted +=
            reminderId =>
            {
                MainThread.BeginInvokeOnMainThread(
                    () => CompleteReminder(
                        reminderId,
                        saveReminders: false));
            };

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
            OrderReminders(
                reminders,
                DateTime.Now).ToList();

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

    private static IEnumerable<ReminderItem> OrderReminders(
    IEnumerable<ReminderItem> source,
    DateTime now)
    {
        var list = source.ToList();

        return list
            // Первичный этап: группы 1 -> 2 -> 3 -> 4
            .GroupBy(r => r.Group)
            .OrderBy(group => group.Key)
            // Вторичный этап: существующая сортировка внутри каждой группы
            .SelectMany(group =>
            {
                var withStart = group
                    .Where(r => r.DisplayStart is not null)
                    .OrderBy(r => r.DisplayStart!.Value)
                    .ThenBy(r => r.Id);

                var withEndOnly = group
                    .Where(r =>
                        r.DisplayStart is null &&
                        r.DisplayEnd is not null)
                    .OrderBy(r => r.DisplayEnd!.Value)
                    .ThenBy(r => r.Id);

                var withoutDates = group
                    .Where(r =>
                        r.DisplayStart is null &&
                        r.DisplayEnd is null)
                    .OrderByDescending(r => r.Id);

                return withStart
                    .Concat(withEndOnly)
                    .Concat(withoutDates);
            });
    }

    private static int GetReminderPriority(
        ReminderItem reminder,
        DateTime now)
    {
        if (reminder.DisplayStart is DateTime displayStart &&
            displayStart >= now)
        {
            return 0;
        }

        if (reminder.DisplayEnd is DateTime displayEnd &&
            displayEnd >= now)
        {
            return 1;
        }

        return 2;
    }

    private static DateTime GetRelevantDisplayTime(
        ReminderItem reminder,
        DateTime now)
    {
        return GetReminderPriority(reminder, now) switch
        {
            0 => reminder.DisplayStart!.Value,
            1 => reminder.DisplayEnd!.Value,
            _ => DateTime.MaxValue,
        };
    }

    private static int GetReminderSortGroup(
        ReminderItem reminder)
    {
        if (reminder.DisplayStart is not null)
        {
            return 0;
        }

        if (reminder.DisplayEnd is not null)
        {
            return 1;
        }

        return 2;
    }

    private static DateTime GetReminderSortTime(
        ReminderItem reminder)
    {
        return GetReminderSortGroup(reminder) switch
        {
            0 => reminder.DisplayStart!.Value,
            1 => reminder.DisplayEnd!.Value,
            _ => DateTime.MaxValue,
        };
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
        if (e.OldItems is not null)
        {
            foreach (ReminderItem reminder in e.OldItems)
            {
                reminder.PropertyChanged -=
                    OnReminderPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ReminderItem reminder in e.NewItems)
            {
                reminder.PropertyChanged +=
                    OnReminderPropertyChanged;
            }
        }

        //if (!isSortingReminders)
        //{
        //    Dispatcher.Dispatch(SortReminders);
        //}
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