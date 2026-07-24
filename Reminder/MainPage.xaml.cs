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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        foreach (ReminderItem reminder in reminders)
        {
            if (ReminderDisplayFormatter.ShouldDisplayNow(reminder, DateTime.Now))
            {
                await notificationService.ShowAsync(reminder);
            }
            else
            {
                notificationService.Cancel(reminder.Id);
            }
        }
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var editorPage = new ReminderEditorPage();
        editorPage.SaveRequested += async (_, editedReminder) =>
        {
            var reminder = new ReminderItem
            {
                Id = GetNextReminderId(),
                Text = editedReminder.Text,
                DisplayStart = editedReminder.DisplayStart,
                DisplayEnd = editedReminder.DisplayEnd,
            };

            reminders.Add(reminder);
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
            reminder.Text = editedReminder.Text;
            reminder.DisplayStart = editedReminder.DisplayStart;
            reminder.DisplayEnd = editedReminder.DisplayEnd;
            RefreshReminders();
            SaveReminders();
            await ShowOrCancelNotificationAsync(reminder);
        };
        editorPage.DeleteRequested += (_, _) => CompleteReminder(reminder);

        await Navigation.PushModalAsync(new NavigationPage(editorPage));
    }

    private void CompleteReminder(ReminderItem reminder)
    {
        reminders.Remove(reminder);
        SaveReminders();
        notificationService.Cancel(reminder.Id);
    }

    private async Task ShowOrCancelNotificationAsync(ReminderItem reminder)
    {
        if (ReminderDisplayFormatter.ShouldDisplayNow(reminder, DateTime.Now))
        {
            await notificationService.ShowAsync(reminder);
        }
        else
        {
            notificationService.Cancel(reminder.Id);
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

    private void SaveReminders()
    {
        store.Save(reminders);
    }
}
