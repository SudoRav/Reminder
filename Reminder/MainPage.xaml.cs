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
            await notificationService.ShowAsync(reminder);
        }
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var editorPage = new ReminderEditorPage();
        editorPage.SaveRequested += async (_, text) =>
        {
            var reminder = new ReminderItem
            {
                Id = GetNextReminderId(),
                Text = text,
            };

            reminders.Add(reminder);
            SaveReminders();
            await notificationService.ShowAsync(reminder);
        };

        await Navigation.PushModalAsync(new NavigationPage(editorPage));
    }

    private async void OnReminderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ReminderItem reminder)
        {
            return;
        }

        var editorPage = new ReminderEditorPage(reminder);
        editorPage.SaveRequested += async (_, text) =>
        {
            reminder.Text = text;
            RemindersCollectionView.ItemsSource = null;
            RemindersCollectionView.ItemsSource = reminders;
            SaveReminders();
            await notificationService.ShowAsync(reminder);
        };
        editorPage.DeleteRequested += (_, _) =>
        {
            reminders.Remove(reminder);
            SaveReminders();
            notificationService.Cancel(reminder.Id);
        };

        await Navigation.PushModalAsync(new NavigationPage(editorPage));
    }

    private int GetNextReminderId()
    {
        return reminders.Count == 0 ? 1 : reminders.Max(static reminder => reminder.Id) + 1;
    }

    private void SaveReminders()
    {
        store.Save(reminders);
    }
}
