using System.Text.Json;

namespace Reminder;

public sealed class ReminderStore
{
    private const string PreferencesKey = "reminders";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ReminderItem> Load()
    {
        string json = Preferences.Default.Get(PreferencesKey, "[]");

        try
        {
            return JsonSerializer.Deserialize<List<ReminderItem>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<ReminderItem> reminders)
    {
        string json = JsonSerializer.Serialize(reminders, JsonOptions);
        Preferences.Default.Set(PreferencesKey, json);
    }
}
