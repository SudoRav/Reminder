using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;

namespace Reminder;

public partial class MainPage : ContentPage
{
    private readonly ObservableCollection<ReminderItem> reminders;
    private readonly ReminderStore store;
    private readonly IReminderNotificationService notificationService;

    public MainPage()
    {
        InitializeComponent();

        store = new ReminderStore();
        notificationService = IPlatformApplication.Current?.Services.GetRequiredService<IReminderNotificationService>()
            ?? throw new InvalidOperationException("Notification service is not registered.");

        reminders = new ObservableCollection<ReminderItem>(store.Load());
        RemindersCollectionView.ItemsSource = reminders;
        SubscribeToNotificationCompletion();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ReloadReminders();

        foreach (ReminderItem reminder in reminders)
        {
            await ShowOrCancelNotificationAsync(reminder);
        }
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var editorPage = new ReminderEditorPage();
        ReminderItem? reminder = null;
        editorPage.SaveRequested += async (_, editedReminder) =>
        {
            if (reminder is null)
            {
                reminder = new ReminderItem
                {
                    Id = GetNextReminderId(),
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
            reminder.NotificationTimes = editedReminder.NotificationTimes;
            RefreshReminders();
            SaveReminders();
            await ShowOrCancelNotificationAsync(reminder);
        };

        await Navigation.PushModalAsync(new NavigationPage(editorPage));
    }

    private async void OnReminderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ReminderItem reminder)
        {
            return;
        }

        await OpenEditorAsync(reminder);
    }

    private void OnCompleteReminderClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is ReminderItem reminder)
        {
            CompleteReminder(reminder);
        }
    }

    private async Task OpenEditorAsync(ReminderItem reminder)
    {
        var editorPage = new ReminderEditorPage(reminder);
        editorPage.SaveRequested += async (_, editedReminder) =>
        {
            notificationService.Cancel(reminder.Id);
            reminder.Text = editedReminder.Text;
            reminder.DisplayStart = editedReminder.DisplayStart;
            reminder.DisplayEnd = editedReminder.DisplayEnd;
            reminder.NotificationTimes = editedReminder.NotificationTimes;
            RefreshReminders();
            SaveReminders();
            await ShowOrCancelNotificationAsync(reminder);
        };
        editorPage.DeleteRequested += (_, _) => CompleteReminder(reminder);

        await Navigation.PushModalAsync(new NavigationPage(editorPage));
    }

    private void CompleteReminder(ReminderItem reminder)
    {
        CompleteReminder(reminder.Id, saveReminders: true);
    }

    private void CompleteReminder(int reminderId, bool saveReminders)
    {
        ReminderItem? reminder = reminders.FirstOrDefault(item => item.Id == reminderId);
        if (reminder is not null)
        {
            reminders.Remove(reminder);
        }

        notificationService.Cancel(reminderId);

        if (saveReminders)
        {
            SaveReminders();
        }
    }

    private async Task ShowOrCancelNotificationAsync(ReminderItem reminder)
    {
        notificationService.Cancel(reminder.Id);

        if (ReminderDisplayFormatter.ShouldDisplayNow(reminder, DateTime.Now))
        {
            await notificationService.ShowAsync(reminder);
        }
        else
        {
            await notificationService.ScheduleAsync(reminder);
        }
    }

    private int GetNextReminderId()
    {
        return reminders.Count == 0 ? 1 : reminders.Max(static reminder => reminder.Id) + 1;
    }

    private void RefreshReminders()
    {
        RemindersCollectionView.ItemsSource = null;
        RemindersCollectionView.ItemsSource = reminders;
    }

    private void ReloadReminders()
    {
        reminders.Clear();
        foreach (ReminderItem reminder in store.Load())
        {
            reminders.Add(reminder);
        }
    }

    private void SubscribeToNotificationCompletion()
    {
#if ANDROID
        AndroidReminderNotificationService.ReminderCompleted += reminderId =>
        {
            MainThread.BeginInvokeOnMainThread(() => CompleteReminder(reminderId, saveReminders: false));
        };
        AndroidReminderNotificationService.ReminderEditorRequested += reminderId =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                ReloadReminders();
                ReminderItem? reminder = reminders.FirstOrDefault(item => item.Id == reminderId);
                if (reminder is not null)
                {
                    await OpenEditorAsync(reminder);
                }
            });
        };
        AndroidReminderNotificationService.NotificationTimeTriggered += (reminderId, notificationTime) =>
        {
            MainThread.BeginInvokeOnMainThread(() => RemoveTriggeredNotificationTime(reminderId, notificationTime));
        };
#endif
    }

    private void RemoveTriggeredNotificationTime(int reminderId, DateTime notificationTime)
    {
        ReminderItem? reminder = reminders.FirstOrDefault(item => item.Id == reminderId);
        if (reminder is null)
        {
            ReloadReminders();
            return;
        }

        if (reminder.NotificationTimes.RemoveAll(time => time == notificationTime) > 0)
        {
            RefreshReminders();
        }
    }

    private void SaveReminders()
    {
        store.Save(reminders);
    }
}
