using System.Globalization;

namespace Reminder;

public sealed class ReminderListPeriodConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ReminderItem reminder ? ReminderDisplayFormatter.GetListDisplayText(reminder) : string.Empty;
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
        return value is ReminderItem reminder && !string.IsNullOrWhiteSpace(ReminderDisplayFormatter.GetListDisplayText(reminder));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
