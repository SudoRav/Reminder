using System.Text.Json;

namespace Reminder;

public sealed class ReminderStore
{
    private const string PreferencesKey = "reminders";
    private const string CompletedPreferencesKey = "completed_reminders";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ReminderItem> Load()
    {
        return LoadFromKey(PreferencesKey);
    }

    public IReadOnlyList<ReminderItem> LoadCompleted()
    {
        List<ReminderItem> completedReminders = LoadFromKey(CompletedPreferencesKey)
            .Where(static reminder => !ShouldRemoveCompletedReminder(reminder, DateTime.Now))
            .ToList();

        SaveCompleted(completedReminders);

        return completedReminders;
    }

    public void Save(IEnumerable<ReminderItem> reminders)
    {
        SaveToKey(PreferencesKey, reminders);
    }

    public void SaveCompleted(IEnumerable<ReminderItem> reminders)
    {
        SaveToKey(CompletedPreferencesKey, reminders);
    }

    private static bool ShouldRemoveCompletedReminder(ReminderItem reminder, DateTime now)
    {
        return reminder.CompletedAt is DateTime completedAt &&
            completedAt.AddMonths(1) <= now;
    }

    private static IReadOnlyList<ReminderItem> LoadFromKey(string key)
    {
        string json = Preferences.Default.Get(key, "[]");

        try
        {
            return JsonSerializer.Deserialize<List<ReminderItem>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void SaveToKey(string key, IEnumerable<ReminderItem> reminders)
    {
        string json = JsonSerializer.Serialize(reminders, JsonOptions);
        Preferences.Default.Set(key, json);
    }
}
