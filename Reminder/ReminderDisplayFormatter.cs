using System.Globalization;

namespace Reminder;

public static class ReminderDisplayFormatter
{
    private static readonly CultureInfo RussianCulture = new("ru-RU");

    public static string GetDisplayText(DateTime? start, DateTime? end, string emptyPeriodText = "Постоянно")
    {
        if (start is null && end is null)
        {
            return emptyPeriodText;
        }

        if (start is not null && end is not null)
        {
            return $"{FormatStart(start.Value)} — {FormatEnd(end.Value)}";
        }

        return start is not null
            ? $"С {FormatStart(start.Value)}"
            : $"По {FormatEnd(end!.Value)}";
    }

    public static string GetDisplayText(ReminderItem reminder, string emptyPeriodText = "Постоянно")
    {
        return GetDisplayText(reminder.DisplayStart, reminder.DisplayEnd, emptyPeriodText);
    }

    public static string FormatNotificationTime(DateTime value)
    {
        return FormatDateTime(value, TimeSpan.MinValue);
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
        return FormatDateTime(value, new TimeSpan(23, 0, 0));
    }

    private static readonly string[] Months =
  {
    "ЯНВ", "ФЕВ", "МАР", "АПР", "МАЙ", "ИЮН",
    "ИЮЛ", "АВГ", "СЕН", "ОКТ", "НОЯ", "ДЕК"
};

    private static string FormatDateTime(DateTime value, TimeSpan hiddenTime)
    {
        string month = Months[value.Month - 1];
        //string date = $"{value:dd.MM} {month} {value:yy}";
        string date = $"{value:dd.MM} {month}";
        return value.TimeOfDay == hiddenTime ? date : $"{date} {value:HH:mm}";
    }
}
