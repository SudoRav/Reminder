using System.Globalization;

namespace Reminder;

public sealed class ReminderListPeriodConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ReminderItem reminder ? ReminderDisplayFormatter.GetDisplayText(reminder, string.Empty) : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ReminderListPeriodVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ReminderItem reminder && !string.IsNullOrWhiteSpace(ReminderDisplayFormatter.GetDisplayText(reminder, string.Empty));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
