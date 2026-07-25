using System.Globalization;

namespace Reminder;

public static class ReminderDisplayFormatter
{
    private static readonly CultureInfo RussianCulture = new("ru-RU"); 

    public static string GetEditorDisplayText(DateTime? start, DateTime? end)
    {
        if (start is null && end is null)
        {
            return "Постоянно";
        }

        if (start is not null && end is not null)
        {
            return $"С {FormatStart(start.Value)} по {FormatEnd(end.Value)}";
        }

        return start is not null
            ? $"С {FormatStart(start.Value)}"
            : $"По {FormatEnd(end!.Value)}";
    }

    public static string GetListDisplayText(ReminderItem reminder)
    {
        if (reminder.DisplayStart is null && reminder.DisplayEnd is null)
        {
            return string.Empty;
        }

        if (reminder.DisplayStart is not null && reminder.DisplayEnd is not null)
        {
            return $"С {FormatStart(reminder.DisplayStart.Value)} по {FormatEnd(reminder.DisplayEnd.Value)}";
        }

        return reminder.DisplayStart is not null
            ? $"С {FormatStart(reminder.DisplayStart.Value)}"
            : $"До {FormatEnd(reminder.DisplayEnd!.Value)}";
    }

    public static bool ShouldDisplayNow(ReminderItem reminder, DateTime now)
    {
        if (reminder.DisplayStart is not null && now < reminder.DisplayStart.Value)
        {
            return false;
        }

        if (reminder.DisplayEnd is not null && now > reminder.DisplayEnd.Value)
        {
            return false;
        }

        return true;
    }

    private static string FormatStart(DateTime value)
    {
        return FormatDateTime(value, new TimeSpan(0, 0, 0));
    }

    private static string FormatEnd(DateTime value)
    {
        return FormatDateTime(value, new TimeSpan(23, 59, 59));
    }

    private static readonly string[] Months =
  {
    "ЯНВ.", "ФЕВ.", "МАР.", "АПР.", "МАЙ.", "ИЮН.",
    "ИЮЛ.", "АВГ.", "СЕН.", "ОКТ.", "НОЯ.", "ДЕК."
};

    private static string FormatDateTime(DateTime value, TimeSpan hiddenTime)
    {
        string month = Months[value.Month - 1];
        string date = $"{value:dd.MM} {month} {value:yy}";
        return value.TimeOfDay == hiddenTime ? date : $"{date} {value:HH:mm}";
    }
}
