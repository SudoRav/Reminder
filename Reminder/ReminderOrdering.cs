namespace Reminder;

/// <summary>
/// Единственное место, где определяется порядок напоминаний.
/// Используется и списком в приложении, и списком в notification center.
/// </summary>
public static class ReminderOrdering
{
    /// <summary>
    /// Группа 1 → 4; внутри группы: со стартом, только с концом, без дат.
    /// </summary>
    public static IEnumerable<ReminderItem> Order(
        IEnumerable<ReminderItem> source)
    {
        List<ReminderItem> list = source.ToList();

        return list
            .GroupBy(r => r.Group)
            .OrderBy(group => group.Key)
            .SelectMany(group =>
            {
                var withStart = group
                    .Where(r => r.DisplayStart is not null)
                    .OrderBy(r => r.DisplayStart!.Value)
                    .ThenBy(r => r.Id);

                var withEndOnly = group
                    .Where(r =>
                        r.DisplayStart is null &&
                        r.DisplayEnd is not null)
                    .OrderBy(r => r.DisplayEnd!.Value)
                    .ThenBy(r => r.Id);

                var withoutDates = group
                    .Where(r =>
                        r.DisplayStart is null &&
                        r.DisplayEnd is null)
                    .OrderByDescending(r => r.Id);

                return withStart
                    .Concat(withEndOnly)
                    .Concat(withoutDates);
            })
            .ToList();
    }
}
