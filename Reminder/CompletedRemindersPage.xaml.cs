using System.Collections.ObjectModel;

namespace Reminder;

public partial class CompletedRemindersPage : ContentPage
{
    private readonly ObservableCollection<ReminderItem> completedReminders;
    private readonly ReminderStore store;

    public CompletedRemindersPage()
    {
        InitializeComponent();

        store = new ReminderStore();

        completedReminders = new ObservableCollection<ReminderItem>(
            store.LoadCompleted()
                .OrderByDescending(r => r.CompletedAt)
        );

        CompletedRemindersCollectionView.ItemsSource = completedReminders;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ReloadCompletedReminders();
    }

    private async void OnRemindersClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private void ReloadCompletedReminders()
    {
        completedReminders.Clear();

        foreach (ReminderItem reminder in store.LoadCompleted()
            .OrderByDescending(r => r.CompletedAt))
        {
            completedReminders.Add(reminder);
        }
    }
}