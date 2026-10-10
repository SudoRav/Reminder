using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using System.Text.Json;

using Orientation = Android.Widget.Orientation;

namespace Reminder;

public sealed class AndroidReminderNotificationService : IReminderNotificationService
{
    // Канал для разовых уведомлений (push, запрос разрешения): высокая важность.
    private const string ChannelId = "persistent_reminders";

    // Канал для закреплённых напоминаний в шторке: тихий, без вибрации.
    internal const string PersistentChannelId =
        "persistent_reminders_silent";

    internal const string OverlayForegroundChannelId =
        "reminder_overlay_foreground";

    internal const string AlarmChannelId =
        "scheduled_reminder_alarms";

    private const string AlarmAction =
        "com.companyname.reminder.SHOW_OVERLAY_REMINDER";

    internal const string CompleteAction =
        "com.companyname.reminder.COMPLETE_REMINDER";

    internal const string OpenEditorAction =
        "com.companyname.reminder.OPEN_REMINDER_EDITOR";

    internal const string StopAlarmAction =
        "com.companyname.reminder.STOP_ALARM";

    internal const string DismissOverlayAction =
        "com.companyname.reminder.DISMISS_OVERLAY";

    private const string AutoCompleteAction =
        "com.companyname.reminder.AUTO_COMPLETE_REMINDER";

    private const string DisplayStartAction =
        "com.companyname.reminder.DISPLAY_START_REMINDER";

    private const string DisplayEndAction =
        "com.companyname.reminder.DISPLAY_END_REMINDER";

    internal const string ReminderIdExtra =
        "reminder_id";

    internal const string NotificationTimeTicksExtra =
        "notification_time_ticks";

    internal const string NotificationOverlayEnabledExtra =
        "notification_overlay_enabled";

    internal const string NotificationPushEnabledExtra =
        "notification_push_enabled";

    internal const string NotificationAlarmEnabledExtra =
        "notification_alarm_enabled";

    internal const int AlarmNotificationIdOffset = 900_000;

    internal const int OverlayForegroundNotificationIdOffset = 10_000;

    internal const int PermissionNotificationIdOffset = 20_000;

    private const int DisplayEndRequestCodeOffset = 30_000;

    private const int DisplayStartRequestCodeOffset = 40_000;

    private readonly Context context;
    private readonly NotificationManager notificationManager;
    private readonly AlarmManager alarmManager;

    public static event Action<int>? ReminderCompleted;

    public static event Action<int>? ReminderEditorRequested;

    public static event Action<int, DateTime>? NotificationTimeTriggered;

    public static event Action<int>? NotificationTimeDeferred;

    internal static void NotifyReminderCompleted(int reminderId) =>
        ReminderCompleted?.Invoke(reminderId);

    internal static void NotifyReminderEditorRequested(int reminderId)
    {
        Action<int>? handler;

        lock (reminderEditorRequestLock)
        {
            handler = ReminderEditorRequested;

            if (handler is null)
            {
                pendingReminderEditorId = reminderId;
                return;
            }
        }

        handler(reminderId);
    }

    internal static int? ConsumePendingReminderEditorRequest()
    {
        lock (reminderEditorRequestLock)
        {
            int? reminderId =
                pendingReminderEditorId;

            pendingReminderEditorId =
                null;

            return reminderId;
        }
    }

    private static readonly object reminderEditorRequestLock = new();

    private static int? pendingReminderEditorId;

    internal static bool IsCompletionAction(string? action) =>
        action == CompleteAction ||
        action == AutoCompleteAction;

    private static void NotifyNotificationTimeTriggered(
        int reminderId,
        DateTime notificationTime) =>
        NotificationTimeTriggered?.Invoke(
            reminderId,
            notificationTime);

    private static void NotifyNotificationTimeDeferred(int reminderId) =>
        NotificationTimeDeferred?.Invoke(reminderId);

    public AndroidReminderNotificationService()
    {
        context = Platform.AppContext;

        notificationManager =
            (NotificationManager)context.GetSystemService(
                Context.NotificationService)!;

        alarmManager =
            (AlarmManager)context.GetSystemService(
                Context.AlarmService)!;

        CreateNotificationChannel();
        CreateAlarmNotificationChannel();
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// Будильники этого напоминания + пересборка всего списка в шторке.
    /// Вызывать ПОСЛЕ сохранения напоминания в хранилище.
    /// </summary>
    public async Task ShowAsync(ReminderItem reminder)
    {
        await ScheduleAsync(reminder);

        SyncPersistentNotificationsFromStore(context);
    }

    /// <summary>
    /// Только будильники и разрешения. Шторку не трогает.
    /// </summary>
    public async Task ScheduleAsync(ReminderItem reminder)
    {
        ScheduleReminderAlarms(reminder);

        await EnsureNotificationPermissionAsync();

        await EnsureOverlayPermissionAsync();
    }

    public void SyncNotificationCenter(
        IReadOnlyList<ReminderItem> orderedReminders) =>
        SyncPersistentNotifications(
            context,
            orderedReminders);

    public void Cancel(int reminderId)
    {
        CancelVisibleNotifications(
            context,
            reminderId);

        CancelNotificationTimeAlarms(
            reminderId);

        CancelDisplayStartAlarm(
            reminderId);

        CancelDisplayEndAlarm(
            reminderId);

        DismissOverlay(
            context,
            reminderId);
    }

    // ============================================================
    // STORAGE
    // ============================================================

    internal static ReminderItem? LoadReminder(int reminderId)
    {
        string json =
            Preferences.Default.Get(
                "reminders",
                "[]");

        try
        {
            return JsonSerializer.Deserialize<List<ReminderItem>>(
                    json,
                    new JsonSerializerOptions(
                        JsonSerializerDefaults.Web))
                ?.FirstOrDefault(
                    reminder => reminder.Id == reminderId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static Intent CreateOpenEditorIntent(int reminderId)
    {
        Intent intent =
            Platform.AppContext.PackageManager?
                .GetLaunchIntentForPackage(
                    Platform.AppContext.PackageName!)
            ?? new Intent(
                Platform.AppContext,
                typeof(MainActivity));

        intent.SetAction(OpenEditorAction);

        intent.SetFlags(
            ActivityFlags.SingleTop |
            ActivityFlags.ClearTop |
            ActivityFlags.NewTask);

        intent.PutExtra(
            ReminderIdExtra,
            reminderId);

        return intent;
    }

    // ============================================================
    // PERSISTENT NOTIFICATIONS (весь список целиком)
    // ============================================================

    private static void EnsurePersistentChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        NotificationManager manager =
            (NotificationManager)context.GetSystemService(
                Context.NotificationService)!;

        if (manager.GetNotificationChannel(PersistentChannelId) is not null)
        {
            return;
        }

        NotificationChannel channel =
            new(
                PersistentChannelId,
                "Список напоминаний",
                NotificationImportance.Low)
            {
                Description =
                    "Закреплённые напоминания в шторке"
            };

        channel.EnableVibration(false);

        channel.SetSound(
            null,
            null);

        channel.SetShowBadge(false);

        manager.CreateNotificationChannel(
            channel);
    }

    /// <summary>
    /// Приводит шторку в соответствие со списком: показывает то, что должно
    /// отображаться, в порядке списка; всё остальное убирает.
    /// </summary>
    internal static void SyncPersistentNotifications(
        Context context,
        IReadOnlyList<ReminderItem> orderedReminders)
    {
        EnsurePersistentChannel(context);

        DateTime now = DateTime.Now;
        HashSet<int> shownIds = [];
        int sortIndex = 0;

        foreach (ReminderItem reminder in orderedReminders)
        {
            bool shouldShow =
                reminder.CompletedAt is null &&
                reminder.ShowInNotificationCenter &&
                ReminderDisplayFormatter.ShouldDisplayNow(
                    reminder,
                    now);

            if (!shouldShow)
            {
                CancelPersistentNotification(
                    context,
                    reminder.Id);

                continue;
            }

            PostPersistentNotification(
                context,
                reminder,
                sortIndex++);

            shownIds.Add(reminder.Id);
        }

        // Уборка «сирот»: persistent-уведомления используют Id == reminder.Id,
        // остальные уведомления приложения имеют Id >= 10 000.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            NotificationManager manager =
                (NotificationManager)context.GetSystemService(
                    Context.NotificationService)!;

            var active =
                manager.GetActiveNotifications();

            if (active is not null)
            {
                foreach (var item in active)
                {
                    if (item.Tag is null &&
                        item.Id < OverlayForegroundNotificationIdOffset &&
                        !shownIds.Contains(item.Id))
                    {
                        manager.Cancel(item.Id);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Для ресиверов и сервисов, где MainPage недоступен.
    /// </summary>
    internal static void SyncPersistentNotificationsFromStore(
        Context context)
    {
        List<ReminderItem> reminders =
            LoadRemindersFromPreferences(
                "reminders",
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web));

        SyncPersistentNotifications(
            context,
            ReminderOrdering.Order(reminders).ToList());
    }

    private static void PostPersistentNotification(
        Context context,
        ReminderItem reminder,
        int sortIndex)
    {
        PendingIntentFlags flags =
            GetImmutableFlags();

        PendingIntent? openPendingIntent =
            PendingIntent.GetActivity(
                context,
                reminder.Id,
                CreateOpenEditorIntent(reminder.Id),
                flags);

        PendingIntent? completePendingIntent =
            PendingIntent.GetBroadcast(
                context,
                reminder.Id,
                CreateCompleteIntent(
                    context,
                    reminder.Id),
                flags);

        Notification notification =
            new NotificationCompat.Builder(
                context,
                PersistentChannelId)

            .SetSmallIcon(
                Resource.Drawable.notification_icon)

//.SetContentTitle(
//    reminder.Text)

.SetContentTitle(
    reminder.Group switch
    {
        1 => $"🟥 {reminder.Text}",
        2 => $"🟨 {reminder.Text}",
        3 => reminder.Text,
        4 => $"🟦 {reminder.Text}",
        _ => reminder.Text
    })

            .SetContentText(
                ReminderDisplayFormatter.GetDisplayText(
                    reminder.DisplayStart,
                    reminder.DisplayEnd))

            .SetStyle(
                new NotificationCompat.BigTextStyle()
                    .BigText(reminder.Text))

            .SetContentIntent(
                openPendingIntent)

            .AddAction(
                Resource.Drawable.notification_icon,
                "Завершить",
                completePendingIntent)

            // Меньший ключ — выше в шторке. Работает среди уведомлений
            // одного канала нашего приложения.
            .SetSortKey(
                sortIndex.ToString("D5"))

            .SetOnlyAlertOnce(true)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetPriority(
                NotificationCompat.PriorityLow)

            .Build();

        NotifyIfEnabled(
            context,
            reminder.Id,
            notification);
    }

    private static void NotifyIfEnabled(
        Context context,
        int notificationId,
        Notification notification)
    {
        NotificationManagerCompat manager =
            NotificationManagerCompat.From(context);

        if (manager.AreNotificationsEnabled())
        {
            manager.Notify(
                notificationId,
                notification);
        }
    }

    private static void CancelPersistentNotification(
        Context context,
        int reminderId)
    {
        NotificationManager manager =
            (NotificationManager)context.GetSystemService(
                Context.NotificationService)!;

        manager.Cancel(reminderId);
    }

    private static Intent CreateCompleteIntent(
        Context context,
        int reminderId)
    {
        Intent completeIntent =
            new(
                context,
                typeof(CompleteReminderReceiver));

        completeIntent.SetAction(
            CompleteAction);

        completeIntent.PutExtra(
            ReminderIdExtra,
            reminderId);

        return completeIntent;
    }

    // ============================================================
    // DISPLAY START / DISPLAY END
    // ============================================================

    private void ScheduleReminderAlarms(
        ReminderItem reminder)
    {
        CancelNotificationTimeAlarms(
            reminder.Id);

        CancelDisplayStartAlarm(
            reminder.Id);

        CancelDisplayEndAlarm(
            reminder.Id);

        ScheduleDisplayStartAlarm(
            reminder);

        ScheduleDisplayEndAlarm(
            reminder);

        ScheduleNotificationTimeAlarms(
            reminder);
    }

    private void ScheduleDisplayStartAlarm(
        ReminderItem reminder)
    {
        if (reminder.DisplayStart is not DateTime displayStart)
        {
            return;
        }

        if (displayStart <= DateTime.Now)
        {
            return;
        }

        PendingIntent? pendingIntent =
            CreateDisplayStartPendingIntent(
                context,
                reminder.Id);

        long triggerAtMillis =
            new DateTimeOffset(
                displayStart)
            .ToUnixTimeMilliseconds();

        ScheduleNotificationTimeAlarm(
            triggerAtMillis,
            pendingIntent);
    }

    private void ScheduleDisplayEndAlarm(
        ReminderItem reminder)
    {
        if (reminder.DisplayEnd is not DateTime displayEnd)
        {
            return;
        }

        if (displayEnd <= DateTime.Now)
        {
            return;
        }

        PendingIntent? pendingIntent =
            CreateDisplayEndPendingIntent(
                context,
                reminder.Id);

        long triggerAtMillis =
            new DateTimeOffset(
                displayEnd)
            .ToUnixTimeMilliseconds();

        ScheduleNotificationTimeAlarm(
            triggerAtMillis,
            pendingIntent);
    }

    private void CancelDisplayStartAlarm(
        int reminderId)
    {
        CancelDisplayStartAlarm(
            context,
            alarmManager,
            reminderId);
    }

    private static void CancelDisplayStartAlarm(
        Context context,
        AlarmManager alarmManager,
        int reminderId)
    {
        PendingIntent? pendingIntent =
            CreateDisplayStartPendingIntent(
                context,
                reminderId);

        if (pendingIntent is null)
        {
            return;
        }

        alarmManager.Cancel(
            pendingIntent);

        pendingIntent.Cancel();
    }

    private void CancelDisplayEndAlarm(
        int reminderId)
    {
        CancelDisplayEndAlarm(
            context,
            alarmManager,
            reminderId);
    }

    private static void CancelDisplayEndAlarm(
        Context context,
        AlarmManager alarmManager,
        int reminderId)
    {
        PendingIntent? pendingIntent =
            CreateDisplayEndPendingIntent(
                context,
                reminderId);

        if (pendingIntent is null)
        {
            return;
        }

        alarmManager.Cancel(
            pendingIntent);

        pendingIntent.Cancel();
    }

    private static PendingIntent? CreateDisplayStartPendingIntent(
        Context context,
        int reminderId)
    {
        Intent intent =
            new(
                context,
                typeof(DisplayStartReminderReceiver));

        intent.SetAction(
            DisplayStartAction);

        intent.PutExtra(
            ReminderIdExtra,
            reminderId);

        return PendingIntent.GetBroadcast(
            context,
            DisplayStartRequestCodeOffset + reminderId,
            intent,
            GetImmutableFlags());
    }

    private static PendingIntent? CreateDisplayEndPendingIntent(
        Context context,
        int reminderId)
    {
        Intent intent =
            new(
                context,
                typeof(DisplayEndReminderReceiver));

        intent.SetAction(
            DisplayEndAction);

        intent.PutExtra(
            ReminderIdExtra,
            reminderId);

        return PendingIntent.GetBroadcast(
            context,
            DisplayEndRequestCodeOffset + reminderId,
            intent,
            GetImmutableFlags());
    }

    // ============================================================
    // NOTIFICATION TIME ALARMS
    // ============================================================

    private void ScheduleNotificationTimeAlarms(
        ReminderItem reminder)
    {
        DateTime now = DateTime.Now;

        foreach (DateTime notificationTime in
                 reminder.NotificationTimes
                     .Where(time => time > now)
                     .Distinct())
        {
            NotificationTimeSettings settings =
                reminder.GetNotificationSettings(
                    notificationTime);

            if (!settings.IsPushEnabled &&
                !settings.IsOverlayEnabled &&
                !settings.IsAlarmEnabled)
            {
                continue;
            }

            PendingIntent? pendingIntent =
                CreateNotificationTimePendingIntent(
                    context,
                    reminder.Id,
                    notificationTime);

            long triggerAtMillis =
                new DateTimeOffset(
                    notificationTime)
                .ToUnixTimeMilliseconds();

            ScheduleNotificationTimeAlarm(
                triggerAtMillis,
                pendingIntent);
        }
    }

    private void ScheduleNotificationTimeAlarm(
        long triggerAtMillis,
        PendingIntent? pendingIntent)
    {
        ScheduleNotificationTimeAlarm(
            context,
            triggerAtMillis,
            pendingIntent);
    }

    private static void ScheduleNotificationTimeAlarm(
        Context context,
        long triggerAtMillis,
        PendingIntent? pendingIntent)
    {
        if (pendingIntent is null)
        {
            return;
        }

        AlarmManager alarmManager =
            (AlarmManager)context.GetSystemService(
                Context.AlarmService)!;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            if (alarmManager.CanScheduleExactAlarms())
            {
                alarmManager.SetExactAndAllowWhileIdle(
                    AlarmType.RtcWakeup,
                    triggerAtMillis,
                    pendingIntent);

                return;
            }
        }
        else
        {
            try
            {
#pragma warning disable CS0618
                alarmManager.SetExactAndAllowWhileIdle(
                    AlarmType.RtcWakeup,
                    triggerAtMillis,
                    pendingIntent);
#pragma warning restore CS0618

                return;
            }
            catch (Java.Lang.SecurityException)
            {
                // Exact alarm unavailable.
            }
        }

        alarmManager.SetAndAllowWhileIdle(
            AlarmType.RtcWakeup,
            triggerAtMillis,
            pendingIntent);
    }

    private void CancelNotificationTimeAlarms(
        int reminderId)
    {
        ReminderItem? reminder =
            LoadReminder(reminderId);

        if (reminder is null)
        {
            return;
        }

        foreach (DateTime notificationTime in
                 reminder.NotificationTimes.Distinct())
        {
            PendingIntent? pendingIntent =
                CreateNotificationTimePendingIntent(
                    context,
                    reminderId,
                    notificationTime);

            if (pendingIntent is null)
            {
                continue;
            }

            alarmManager.Cancel(
                pendingIntent);

            pendingIntent.Cancel();
        }
    }

    private static PendingIntent? CreateNotificationTimePendingIntent(
        Context context,
        int reminderId,
        DateTime notificationTime)
    {
        Intent intent =
            new(
                context,
                typeof(OverlayReminderReceiver));

        intent.SetAction(
            AlarmAction);

        intent.PutExtra(
            ReminderIdExtra,
            reminderId);

        intent.PutExtra(
            NotificationTimeTicksExtra,
            notificationTime.Ticks);

        return PendingIntent.GetBroadcast(
            context,
            GetNotificationTimeRequestCode(
                reminderId,
                notificationTime),
            intent,
            GetImmutableFlags());
    }

    private static int GetNotificationTimeRequestCode(
        int reminderId,
        DateTime notificationTime)
    {
        unchecked
        {
            int hash = 17;

            hash =
                (hash * 31) +
                reminderId;

            hash =
                (hash * 31) +
                notificationTime.Ticks.GetHashCode();

            return hash;
        }
    }

    private static PendingIntentFlags GetImmutableFlags()
    {
        PendingIntentFlags flags =
            PendingIntentFlags.UpdateCurrent;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        return flags;
    }

    // ============================================================
    // SHOW OVERLAY
    // ============================================================

    // ОСТАВЛЕН БЕЗ ИЗМЕНЕНИЙ ПО ВАШЕМУ ПРОСЬБЕ.
    internal static void ShowOverlay(
        Context context,
        ReminderItem reminder,
        DateTime? notificationTime)
    {
        NotificationTimeSettings settings =
            notificationTime.HasValue
                ? reminder.GetNotificationSettings(
                    notificationTime.Value)
                : new NotificationTimeSettings
                {
                    IsPushEnabled = true,
                    IsOverlayEnabled = true,
                    IsAlarmEnabled = true
                };

        // NotificationTime — одноразовое событие.
        // Оно должно быть удалено независимо от того,
        // какие способы уведомления включены.
        ReminderItem reminderToShow = reminder;

        if (notificationTime.HasValue)
        {
            reminderToShow =
                RemoveNotificationTime(
                    reminder.Id,
                    notificationTime.Value)
                ?? reminder;
        }

        // Push — самостоятельный способ уведомления.
        // Он не зависит от Overlay или Alarm.
        if (settings.IsPushEnabled)
        {
            ShowScheduledPushNotification(
                context,
                reminderToShow);
        }

        // Overlay — самостоятельный способ уведомления.
        if (settings.IsOverlayEnabled)
        {
            if (!CanDrawOverlay(context))
            {
                ShowPermissionRequiredNotification(
                    context,
                    reminderToShow);
            }
            else
            {
                StartOverlayService(
                    context,
                    reminderToShow,
                    notificationTime,
                    settings);
            }
        }
        // Если Overlay выключен, но Alarm включен,
        // всё равно нужно запустить сервис для Alarm.
        else if (settings.IsAlarmEnabled)
        {
            StartOverlayService(
                context,
                reminderToShow,
                notificationTime,
                settings);
        }
    }

    private static void StartOverlayService(
        Context context,
        ReminderItem reminder,
        DateTime? notificationTime,
        NotificationTimeSettings settings)
    {
        Intent serviceIntent =
            new(
                context,
                typeof(ReminderOverlayService));

        serviceIntent.PutExtra(
            ReminderIdExtra,
            reminder.Id);

        if (notificationTime.HasValue)
        {
            serviceIntent.PutExtra(
                NotificationTimeTicksExtra,
                notificationTime.Value.Ticks);

            serviceIntent.PutExtra(
                NotificationOverlayEnabledExtra,
                settings.IsOverlayEnabled);

            serviceIntent.PutExtra(
                NotificationPushEnabledExtra,
                settings.IsPushEnabled);

            serviceIntent.PutExtra(
                NotificationAlarmEnabledExtra,
                settings.IsAlarmEnabled);
        }

        ContextCompat.StartForegroundService(
            context,
            serviceIntent);
    }

    // ============================================================
    // NOTIFICATION TIME STORAGE
    // ============================================================

    private static ReminderItem? RemoveNotificationTime(
        int reminderId,
        DateTime notificationTime)
    {
        const string remindersKey = "reminders";

        string json =
            Preferences.Default.Get(
                remindersKey,
                "[]");

        List<ReminderItem> reminders;

        try
        {
            reminders =
                JsonSerializer.Deserialize<List<ReminderItem>>(
                    json,
                    new JsonSerializerOptions(
                        JsonSerializerDefaults.Web))
                ?? [];
        }
        catch (JsonException)
        {
            return null;
        }

        ReminderItem? reminder =
            reminders.FirstOrDefault(
                item => item.Id == reminderId);

        if (reminder is null)
        {
            return null;
        }

        int removedCount =
            reminder.NotificationTimes.RemoveAll(
                time => time == notificationTime);

        reminder.NotificationTimeSettings.RemoveAll(
            settings => settings.Time == notificationTime);

        if (removedCount == 0)
        {
            return reminder;
        }

        Preferences.Default.Set(
            remindersKey,
            JsonSerializer.Serialize(
                reminders,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web)));

        MainThread.BeginInvokeOnMainThread(
            () =>
                NotifyNotificationTimeTriggered(
                    reminderId,
                    notificationTime));

        return reminder;
    }

    internal static void DeferNotificationTime(
        Context context,
        int reminderId,
        NotificationTimeSettings originalSettings)
    {
        const string remindersKey = "reminders";

        JsonSerializerOptions jsonOptions =
            new(JsonSerializerDefaults.Web);

        List<ReminderItem> reminders =
            LoadRemindersFromPreferences(
                remindersKey,
                jsonOptions);

        ReminderItem? reminder = reminders.FirstOrDefault(
            item => item.Id == reminderId);

        if (reminder is null || reminder.CompletedAt is not null)
        {
            return;
        }

        DateTime deferredTime = DateTime.Now.AddHours(1);

        NotificationTimeSettings deferredSettings = new()
        {
            Time = deferredTime,
            IsPushEnabled = originalSettings.IsPushEnabled,
            IsOverlayEnabled = originalSettings.IsOverlayEnabled,
            IsAlarmEnabled = originalSettings.IsAlarmEnabled
        };

        reminder.NotificationTimes.Add(deferredTime);
        reminder.NotificationTimeSettings.Add(deferredSettings);

        Preferences.Default.Set(
            remindersKey,
            JsonSerializer.Serialize(reminders, jsonOptions));

        PendingIntent? pendingIntent =
            CreateNotificationTimePendingIntent(
                context,
                reminderId,
                deferredTime);

        ScheduleNotificationTimeAlarm(
            context,
            new DateTimeOffset(deferredTime).ToUnixTimeMilliseconds(),
            pendingIntent);

        MainThread.BeginInvokeOnMainThread(
            () => NotifyNotificationTimeDeferred(reminderId));
    }

    // ============================================================
    // ALARM CANCELLATION / COMPLETION
    // ============================================================

    internal static void CancelScheduledAlarms(
        Context context,
        int reminderId,
        ReminderItem? reminder)
    {
        AlarmManager alarmManager =
            (AlarmManager)context.GetSystemService(
                Context.AlarmService)!;

        if (reminder is not null)
        {
            foreach (DateTime notificationTime in
                     reminder.NotificationTimes.Distinct())
            {
                PendingIntent? pendingIntent =
                    CreateNotificationTimePendingIntent(
                        context,
                        reminder.Id,
                        notificationTime);

                if (pendingIntent is null)
                {
                    continue;
                }

                alarmManager.Cancel(
                    pendingIntent);

                pendingIntent.Cancel();
            }
        }

        CancelDisplayStartAlarm(
            context,
            alarmManager,
            reminderId);

        CancelDisplayEndAlarm(
            context,
            alarmManager,
            reminderId);
    }

    internal static bool CompleteReminderInStore(
        int reminderId)
    {
        const string remindersKey = "reminders";

        const string completedRemindersKey =
            "completed_reminders";

        JsonSerializerOptions jsonOptions =
            new(JsonSerializerDefaults.Web);

        List<ReminderItem> reminders =
            LoadRemindersFromPreferences(
                remindersKey,
                jsonOptions);

        ReminderItem? reminder =
            reminders.FirstOrDefault(
                item => item.Id == reminderId);

        if (reminder is null)
        {
            return false;
        }

        reminders.Remove(reminder);

        reminder.CompletedAt =
            DateTime.Now;

        reminder.NotificationTimes.Clear();

        reminder.NotificationTimeSettings.Clear();

        List<ReminderItem> completedReminders =
            LoadRemindersFromPreferences(
                completedRemindersKey,
                jsonOptions);

        completedReminders.RemoveAll(
            item => item.Id == reminderId);

        completedReminders.Add(
            reminder);

        Preferences.Default.Set(
            remindersKey,
            JsonSerializer.Serialize(
                reminders,
                jsonOptions));

        Preferences.Default.Set(
            completedRemindersKey,
            JsonSerializer.Serialize(
                completedReminders,
                jsonOptions));

        return true;
    }

    private static List<ReminderItem> LoadRemindersFromPreferences(
        string key,
        JsonSerializerOptions jsonOptions)
    {
        string json =
            Preferences.Default.Get(
                key,
                "[]");

        try
        {
            return JsonSerializer.Deserialize<List<ReminderItem>>(
                       json,
                       jsonOptions)
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ============================================================
    // NOTIFICATION CANCELLATION
    // ============================================================

    internal static void CancelVisibleNotifications(
        Context context,
        int reminderId)
    {
        NotificationManager manager =
            (NotificationManager)context.GetSystemService(
                Context.NotificationService)!;

        manager.Cancel(reminderId);

        manager.Cancel(
            AlarmNotificationIdOffset + reminderId);

        manager.Cancel(
            OverlayForegroundNotificationIdOffset + reminderId);

        manager.Cancel(
            PermissionNotificationIdOffset + reminderId);
    }

    internal static void CancelOverlayNotifications(
        Context context,
        int reminderId)
    {
        NotificationManager manager =
            (NotificationManager)context.GetSystemService(
                Context.NotificationService)!;

        manager.Cancel(
            AlarmNotificationIdOffset + reminderId);

        manager.Cancel(
            OverlayForegroundNotificationIdOffset + reminderId);

        manager.Cancel(
            PermissionNotificationIdOffset + reminderId);
    }

    /// <summary>
    /// Пересобирает весь список в шторке из хранилища, чтобы порядок
    /// всегда совпадал с приложением. Параметр оставлен для совместимости.
    /// </summary>
    internal static void RestorePersistentNotification(
        Context context,
        int reminderId)
    {
        SyncPersistentNotificationsFromStore(context);
    }

    internal static void DismissOverlay(Context context, int reminderId)
    {
        CancelOverlayNotifications(context, reminderId);

        ReminderOverlayService? service =
            ReminderOverlayService.GetCurrentInstance();

        service?.DismissReminderOverlay(reminderId);
    }

    // ============================================================
    // PERMISSIONS
    // ============================================================

    private static bool CanDrawOverlay(
        Context context) =>
        Build.VERSION.SdkInt < BuildVersionCodes.M ||
        Settings.CanDrawOverlays(context);

    private static Task EnsureOverlayPermissionAsync()
    {
        if (CanDrawOverlay(
                Platform.AppContext))
        {
            return Task.CompletedTask;
        }

        Intent settingsIntent =
            new(
                Settings.ActionManageOverlayPermission,
                Android.Net.Uri.Parse(
                    $"package:{Platform.AppContext.PackageName}"));

        settingsIntent.SetFlags(
            ActivityFlags.NewTask);

        Platform.AppContext.StartActivity(
            settingsIntent);

        return Task.CompletedTask;
    }

    internal static void ShowPermissionRequiredNotification(
        Context context,
        ReminderItem reminder)
    {
        PendingIntentFlags flags =
            PendingIntentFlags.UpdateCurrent;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        Intent settingsIntent =
            new(
                Settings.ActionManageOverlayPermission,
                Android.Net.Uri.Parse(
                    $"package:{context.PackageName}"));

        PendingIntent? settingsPendingIntent =
            PendingIntent.GetActivity(
                context,
                reminder.Id,
                settingsIntent,
                flags);

        Notification notification =
            new NotificationCompat.Builder(
                context,
                ChannelId)

            .SetSmallIcon(
                Resource.Drawable.notification_icon)

            .SetContentTitle(
                "Разрешите показ поверх окон")

            .SetContentText(
                reminder.Text)

            .SetStyle(
                new NotificationCompat.BigTextStyle()
                    .BigText(reminder.Text))

            .SetContentIntent(
                settingsPendingIntent)

            .SetAutoCancel(true)

            .SetPriority(
                NotificationCompat.PriorityHigh)

            .Build();

        ((NotificationManager)context.GetSystemService(
                Context.NotificationService)!)
            .Notify(
                PermissionNotificationIdOffset + reminder.Id,
                notification);
    }

    private static async Task<bool> EnsureNotificationPermissionAsync()
    {
        if (Build.VERSION.SdkInt <
            BuildVersionCodes.Tiramisu)
        {
            return true;
        }

        if (ContextCompat.CheckSelfPermission(
                Platform.AppContext,
                Manifest.Permission.PostNotifications)
            == Permission.Granted)
        {
            return true;
        }

        PermissionStatus status =
            await Permissions.RequestAsync<
                PostNotificationsPermission>();

        return status ==
               PermissionStatus.Granted;
    }

    private sealed class PostNotificationsPermission
        : Permissions.BasePlatformPermission
    {
        public override (
            string androidPermission,
            bool isRuntime)[] RequiredPermissions =>
            [
                (
                    Manifest.Permission.PostNotifications,
                    true)
            ];
    }

    // ============================================================
    // NOTIFICATION CHANNELS
    // ============================================================

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt <
            BuildVersionCodes.O)
        {
            return;
        }

        NotificationChannel channel =
            new(
                ChannelId,
                "Постоянные напоминания",
                NotificationImportance.High)
            {
                Description =
                    "Липкие уведомления"
            };

        channel.EnableVibration(true);

        channel.SetVibrationPattern(
            new long[]
            {
                0,
                300,
                150,
                300
            });

        channel.SetShowBadge(true);

        notificationManager.CreateNotificationChannel(
            channel);

        EnsurePersistentChannel(context);
    }

    private void CreateAlarmNotificationChannel()
    {
        if (Build.VERSION.SdkInt <
            BuildVersionCodes.O)
        {
            return;
        }

        NotificationChannel channel =
            new(
                AlarmChannelId,
                "Будильники напоминаний",
                NotificationImportance.Max)
            {
                Description =
                    "Громкие уведомления в назначенное время"
            };

        // Звук и вибрация Alarm реализуются через MediaPlayer
        // и Vibrator в ReminderOverlayService.
        channel.EnableVibration(false);

        channel.SetSound(
            null,
            null);

        channel.LockscreenVisibility =
            NotificationVisibility.Public;

        notificationManager.CreateNotificationChannel(
            channel);
    }

    // ============================================================
    // PUSH
    // ============================================================

    private static void ShowScheduledPushNotification(
    Context context,
    ReminderItem reminder)
    {
        DateTime now = DateTime.Now;

        if (!ReminderDisplayFormatter.ShouldDisplayNow(
                reminder,
                now))
        {
            return;
        }

        PendingIntentFlags flags =
            PendingIntentFlags.UpdateCurrent;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        PendingIntent? pendingIntent =
            PendingIntent.GetActivity(
                context,
                reminder.Id,
                CreateOpenEditorIntent(
                    reminder.Id),
                flags);

        Notification notification =
            new NotificationCompat.Builder(
                context,
                ChannelId)

            .SetSmallIcon(
                Resource.Drawable.notification_icon)

.SetContentTitle(
    reminder.Group switch
    {
        1 => $"🟥 {reminder.Text}",
        2 => $"🟨 {reminder.Text}",
        3 => reminder.Text,
        4 => $"🟦 {reminder.Text}",
        _ => reminder.Text
    })

            .SetContentText(
                ReminderDisplayFormatter.GetDisplayText(
                    reminder.DisplayStart,
                    reminder.DisplayEnd))

            .SetStyle(
                new NotificationCompat.BigTextStyle()
                    .BigText(reminder.Text))

            .SetContentIntent(
                pendingIntent)

            // Push — самостоятельное одноразовое уведомление.
            // Кнопки "Завершить" здесь НЕТ.
            .SetOngoing(false)
            .SetAutoCancel(true)

            .SetPriority(
                NotificationCompat.PriorityHigh)

            .Build();

        NotificationManagerCompat manager =
            NotificationManagerCompat.From(context);

        if (manager.AreNotificationsEnabled())
        {
            /*
             * Push — одноразовое уведомление.
             *
             * Используется отдельный ID,
             * чтобы Push не заменял persistent notification.
             */
            int notificationId =
                PermissionNotificationIdOffset +
                100_000 +
                reminder.Id;

            manager.Notify(
                notificationId,
                notification);
        }
    }
}


// ================================================================
// DISPLAY START RECEIVER
// ================================================================

[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public sealed class DisplayStartReminderReceiver
    : BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        int reminderId =
            intent?.GetIntExtra(
                AndroidReminderNotificationService.ReminderIdExtra,
                0) ?? 0;

        if (reminderId == 0)
        {
            return;
        }

        ReminderItem? reminder =
            AndroidReminderNotificationService.LoadReminder(
                reminderId);

        if (reminder is null)
        {
            return;
        }

        if (reminder.CompletedAt is not null)
        {
            AndroidReminderNotificationService
                .CancelVisibleNotifications(
                    context,
                    reminderId);

            return;
        }

        DateTime now = DateTime.Now;

        /*
         * DisplayStart alarm мог прийти с небольшой задержкой.
         * Поэтому ещё раз проверяем реальное состояние.
         */
        if (reminder.DisplayStart is DateTime displayStart &&
            now < displayStart)
        {
            return;
        }

        if (reminder.DisplayEnd is DateTime displayEnd &&
            now > displayEnd)
        {
            AndroidReminderNotificationService
                .CancelVisibleNotifications(
                    context,
                    reminderId);

            return;
        }

        // Пересобирает весь список, чтобы порядок совпадал с приложением.
        AndroidReminderNotificationService
            .RestorePersistentNotification(
                context,
                reminderId);
    }
}


// ================================================================
// DISPLAY END RECEIVER
// ================================================================

[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public sealed class DisplayEndReminderReceiver
    : BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        int reminderId =
            intent?.GetIntExtra(
                AndroidReminderNotificationService.ReminderIdExtra,
                0) ?? 0;

        if (reminderId == 0)
        {
            return;
        }

        ReminderItem? reminder =
            AndroidReminderNotificationService.LoadReminder(
                reminderId);

        if (reminder is null)
        {
            return;
        }

        /*
         * DisplayEnd всегда означает:
         *
         * persistent notification больше
         * не должно существовать.
         */
        AndroidReminderNotificationService
            .CancelVisibleNotifications(
                context,
                reminderId);

        /*
         * После DisplayEnd дополнительные
         * NotificationTime больше не должны
         * запускаться.
         */
        AndroidReminderNotificationService
            .CancelScheduledAlarms(
                context,
                reminderId,
                reminder);

        /*
         * Сам ReminderItem переносим в Completed
         * только если это явно разрешено.
         */
        bool completed =
            reminder.AutoCompleteOnDisplayEnd &&
            AndroidReminderNotificationService
                .CompleteReminderInStore(reminderId);

        AndroidReminderNotificationService
            .SyncPersistentNotificationsFromStore(context);

        if (completed)
        {
            MainThread.BeginInvokeOnMainThread(
                () =>
                    AndroidReminderNotificationService
                        .NotifyReminderCompleted(
                            reminderId));
        }
    }
}


// ================================================================
// NOTIFICATION TIME RECEIVER
// ================================================================

[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public sealed class OverlayReminderReceiver
    : BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        int reminderId =
            intent?.GetIntExtra(
                AndroidReminderNotificationService.ReminderIdExtra,
                0) ?? 0;

        if (reminderId == 0)
        {
            return;
        }

        long notificationTimeTicks =
            intent?.GetLongExtra(
                AndroidReminderNotificationService.NotificationTimeTicksExtra,
                0L) ?? 0L;

        DateTime? notificationTime =
            notificationTimeTicks == 0L
                ? null
                : new DateTime(
                    notificationTimeTicks);

        ReminderItem? reminder =
            AndroidReminderNotificationService.LoadReminder(
                reminderId);

        if (reminder is null)
        {
            return;
        }

        /*
         * Если ReminderItem уже завершён,
         * дополнительное уведомление не запускаем.
         */
        if (reminder.CompletedAt is not null)
        {
            return;
        }

        /*
         * Если NotificationTime оказался за пределами
         * DisplayStart/DisplayEnd, Push/Overlay/Alarm
         * всё равно должны соблюдать жизненный цикл
         * самого ReminderItem.
         */
        if (!ReminderDisplayFormatter.ShouldDisplayNow(
                reminder,
                DateTime.Now))
        {
            return;
        }

        AndroidReminderNotificationService.ShowOverlay(
            context,
            reminder,
            notificationTime);
    }
}


// ================================================================
// COMPLETE RECEIVER
// ================================================================

[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public sealed class CompleteReminderReceiver
    : BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        if (context is null ||
            !AndroidReminderNotificationService
                .IsCompletionAction(intent?.Action))
        {
            return;
        }

        int reminderId =
            intent!.GetIntExtra(
                AndroidReminderNotificationService.ReminderIdExtra,
                0);

        if (reminderId == 0)
        {
            return;
        }

        ReminderItem? reminder =
            AndroidReminderNotificationService
                .LoadReminder(reminderId);

        /*
         * Сначала отменяем ВСЕ Android alarm'ы,
         * включая DisplayStart и DisplayEnd.
         */
        AndroidReminderNotificationService
            .CancelScheduledAlarms(
                context,
                reminderId,
                reminder);

        /*
         * Затем переносим ReminderItem
         * из reminders в completed_reminders.
         */
        bool completed =
            AndroidReminderNotificationService
                .CompleteReminderInStore(
                    reminderId);

        /*
         * Удаляем всё, что может оставаться
         * в шторке/overlay.
         */
        AndroidReminderNotificationService
            .CancelVisibleNotifications(
                context,
                reminderId);

        AndroidReminderNotificationService
            .DismissOverlay(
                context,
                reminderId);

        /*
         * Пересобираем список в шторке из хранилища.
         */
        AndroidReminderNotificationService
            .SyncPersistentNotificationsFromStore(context);

        if (completed)
        {
            MainThread.BeginInvokeOnMainThread(
                () =>
                    AndroidReminderNotificationService
                        .NotifyReminderCompleted(
                            reminderId));
        }
    }
}


[Service(Enabled = true, Exported = false)]
public sealed class ReminderOverlayService : Service
{
    private static ReminderOverlayService? Current;

    internal static ReminderOverlayService? GetCurrentInstance()
    {
        return Current;
    }

    private WindowManagerLayoutParams? layoutParams;
    private IWindowManager? windowManager;
    private Android.Views.View? overlayView;
    private Queue<PendingOverlay> pendingOverlays = [];
    private int displayedReminderId;
    private MediaPlayer? alarmPlayer;
    private Vibrator? vibrator;
    private BroadcastReceiver? unlockReceiver;
    private bool isUnlockReceiverRegistered;
    private int reminderId;

    private sealed record PendingOverlay(
        ReminderItem Reminder,
        NotificationTimeSettings Settings);

    public override void OnCreate()
    {
        base.OnCreate();

        Current = this;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            NotificationManager manager =
                (NotificationManager)GetSystemService(
                    NotificationService)!;

            NotificationChannel channel =
                new NotificationChannel(
                    AndroidReminderNotificationService.OverlayForegroundChannelId,
                    "Служба напоминаний",
                    NotificationImportance.Min)
                {
                    Description = "Техническое уведомление службы напоминаний"
                };

            channel.EnableVibration(false);
            channel.SetSound(null, null);
            channel.SetShowBadge(false);

            manager.CreateNotificationChannel(channel);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        StartForeground(
            AndroidReminderNotificationService.OverlayForegroundNotificationIdOffset,
            BuildForegroundNotification());

        reminderId =
            intent?.GetIntExtra(
                AndroidReminderNotificationService.ReminderIdExtra,
                0) ?? 0;

        if (intent?.Action ==
            AndroidReminderNotificationService.DismissOverlayAction)
        {
            DismissReminderOverlay(reminderId);
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action ==
            AndroidReminderNotificationService.CompleteAction)
        {
            DismissReminderOverlay(reminderId);
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action ==
            AndroidReminderNotificationService.StopAlarmAction)
        {
            StopAlarmAfterUnlock();
            return StartCommandResult.NotSticky;
        }

        long notificationTimeTicks =
            intent?.GetLongExtra(
                AndroidReminderNotificationService.NotificationTimeTicksExtra,
                0L) ?? 0L;

        DateTime? notificationTime =
            notificationTimeTicks == 0L
                ? null
                : new DateTime(notificationTimeTicks);

        ReminderItem? reminder =
            AndroidReminderNotificationService.LoadReminder(reminderId);

        if (reminder is null)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        NotificationTimeSettings settings =
            notificationTime.HasValue
                ? new NotificationTimeSettings
                {
                    Time = notificationTime.Value,

                    IsOverlayEnabled =
                        intent?.GetBooleanExtra(
                            AndroidReminderNotificationService.NotificationOverlayEnabledExtra,
                            false) ?? false,

                    IsPushEnabled =
                        intent?.GetBooleanExtra(
                            AndroidReminderNotificationService.NotificationPushEnabledExtra,
                            false) ?? false,

                    IsAlarmEnabled =
                        intent?.GetBooleanExtra(
                            AndroidReminderNotificationService.NotificationAlarmEnabledExtra,
                            false) ?? false
                }
                : new NotificationTimeSettings
                {
                    IsOverlayEnabled = true,
                    IsAlarmEnabled = true
                };

        if (settings.IsOverlayEnabled)
        {
            AddOverlay(reminder, settings);
        }
        else if (settings.IsAlarmEnabled)
        {
            TriggerAlert(reminder);
        }

        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        RemoveOverlay();
        StopAlarmSignal();

        base.OnDestroy();
    }

    private Notification BuildForegroundNotification()
    {
        return new NotificationCompat.Builder(
            this,
            AndroidReminderNotificationService.OverlayForegroundChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle("Служба напоминаний")
            .SetContentText("Показ окна напоминания")
            .SetPriority(NotificationCompat.PriorityMin)
            .SetSilent(true)
            .SetOngoing(true)
            .SetLocalOnly(true)
            .Build();
    }

    private void AddOverlay(ReminderItem reminder, NotificationTimeSettings settings)
    {
        // Android запускает один экземпляр Service. Когда несколько alarm'ов
        // срабатывают одновременно, новый StartCommand не должен удалять уже
        // показанное окно: оно помещается в очередь и будет показано после
        // закрытия текущего.
        if (overlayView is not null)
        {
            pendingOverlays.Enqueue(new PendingOverlay(reminder, settings));
            return;
        }

        displayedReminderId = reminder.Id;

        if (settings.IsAlarmEnabled)
        {
            TriggerAlert(reminder);
        }

        windowManager = GetSystemService(WindowService).JavaCast<IWindowManager>();

        var metrics = Resources.DisplayMetrics;
        int screenWidth = metrics.WidthPixels;
        int screenHeight = metrics.HeightPixels;

        // Определяем ориентацию
        bool isLandscape = screenWidth > screenHeight;

        // Размер карточки в зависимости от ориентации
        int overlayWidth;
        int overlayHeight;

        if (isLandscape)
        {
            // В горизонтальной ориентации делаем карточку шире и ниже
            overlayWidth = (int)(screenWidth * 0.6f);  // Увеличиваем ширину
            overlayHeight = (int)(screenHeight * 0.6f); // Увеличиваем высоту
                                                        // Ограничиваем максимальные размеры
            overlayWidth = Math.Min(overlayWidth, (int)(screenHeight * 0.9f));
            overlayHeight = Math.Min(overlayHeight, (int)(screenHeight * 0.9f));
        }
        else
        {
            // Вертикальная ориентация - оставляем как было
            overlayWidth = (int)(screenWidth * 0.8f);
            overlayHeight = (int)(screenHeight * 0.33f);
            // Ограничиваем минимальную высоту
            overlayHeight = Math.Max(overlayHeight, 300);
        }

        // Дополнительная проверка на слишком маленькие размеры
        overlayWidth = Math.Max(overlayWidth, 400);
        overlayHeight = Math.Max(overlayHeight, 300);

        var root = new Android.Widget.FrameLayout(this);
        root.SetBackgroundColor(Android.Graphics.Color.Transparent);
        root.Clickable = true;

        var card = new Android.Widget.LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Clickable = true
        };

        EventHandler openReminderEditor = (_, _) =>
        {
            StartActivity(
                AndroidReminderNotificationService.CreateOpenEditorIntent(
                    reminder.Id));
            RemoveOverlay();
            ShowNextOverlay();
        };

        // Нажатие по карточке открывает напоминание. Кнопки имеют собственные
        // обработчики, поэтому это действие не применяется к ним.
        card.Click += openReminderEditor;

        var cardBackground = new Android.Graphics.Drawables.GradientDrawable();
        cardBackground.SetColor(Android.Graphics.Color.White);
        cardBackground.SetCornerRadius(32);
        card.Background = cardBackground;

        // Адаптивные отступы в зависимости от ориентации
        int paddingHorizontal = isLandscape ? 60 : 40;
        int paddingVertical = isLandscape ? 32 : 24;
        card.SetPadding(paddingHorizontal, paddingVertical, paddingHorizontal, paddingVertical);

        // Заголовок
        var header = new Android.Widget.LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Clickable = true
        };
        header.Click += openReminderEditor;

        var title = new Android.Widget.TextView(this)
        {
            Text = $"{ReminderDisplayFormatter.GetDisplayText(reminder)}",
            TextSize = isLandscape ? 18 : 14 // Увеличиваем шрифт в горизонтальной ориентации
        };
        title.SetTextColor(Android.Graphics.Color.Black);
        title.Click += openReminderEditor;

        header.AddView(
            title,
            new Android.Widget.LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WrapContent,
                1f));

        var closeButton = new Android.Widget.TextView(this)
        {
            Text = "✕",
            TextSize = isLandscape ? 48 : 40,
            Gravity = GravityFlags.Center
        };
        closeButton.SetTextColor(Android.Graphics.Color.Black);
        closeButton.SetPadding(24, 0, 0, 0);

        closeButton.Click += (_, _) =>
        {
            RemoveOverlay();
            AndroidReminderNotificationService.RestorePersistentNotification(this, reminder.Id);
            ShowNextOverlay();
        };

        header.AddView(
            closeButton,
            new Android.Widget.LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent));

        card.AddView(
            header,
            new Android.Widget.LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent));

        // Текст с адаптивным размером
        var textView = new Android.Widget.TextView(this)
        {
            Text = reminder.Text,
            TextSize = isLandscape ? 22 : 18
        };
        textView.SetTextColor(Android.Graphics.Color.Black);

        var scrollView = new Android.Widget.ScrollView(this);
        scrollView.AddView(textView);
        scrollView.Clickable = true;
        scrollView.Click += openReminderEditor;
        textView.Click += openReminderEditor;

        var scrollParams = new Android.Widget.LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f);
        card.AddView(scrollView, scrollParams);

        // Кнопки
        var buttonGrid = new Android.Widget.GridLayout(this)
        {
            ColumnCount = 2,
            RowCount = 1
        };

        // Адаптивные кнопки
        var deferButton = new Android.Widget.Button(this)
        {
            Text = "Отложить",
            TextSize = isLandscape ? 18 : 14
        };
        deferButton.Click += (_, _) =>
        {
            AndroidReminderNotificationService.DeferNotificationTime(
                this,
                reminder.Id,
                settings);
            RemoveOverlay();
            AndroidReminderNotificationService.RestorePersistentNotification(
                this,
                reminder.Id);
            ShowNextOverlay();
        };

        var completeButton = new Android.Widget.Button(this)
        {
            Text = "Завершить",
            TextSize = isLandscape ? 18 : 14
        };
        completeButton.Click += (_, _) =>
        {
            SendBroadcast(
                new Android.Content.Intent(
                    this,
                    typeof(CompleteReminderReceiver))
                    .SetAction(AndroidReminderNotificationService.CompleteAction)
                    .PutExtra(
                        AndroidReminderNotificationService.ReminderIdExtra,
                        reminder.Id));
            RemoveOverlay();
            ShowNextOverlay();
        };

        var deferButtonParams = new Android.Widget.GridLayout.LayoutParams
        {
            Width = 0,
            Height = ViewGroup.LayoutParams.WrapContent,
            ColumnSpec = Android.Widget.GridLayout.InvokeSpec(0, 1, 1f)
        };

        var completeButtonParams = new Android.Widget.GridLayout.LayoutParams
        {
            Width = 0,
            Height = ViewGroup.LayoutParams.WrapContent,
            ColumnSpec = Android.Widget.GridLayout.InvokeSpec(1, 1, 1f)
        };

        buttonGrid.AddView(deferButton, deferButtonParams);
        buttonGrid.AddView(completeButton, completeButtonParams);

        var buttonGridParams = new Android.Widget.LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = isLandscape ? 32 : 24
        };
        card.AddView(buttonGrid, buttonGridParams);

        var cardParams = new Android.Widget.FrameLayout.LayoutParams(
            overlayWidth,
            overlayHeight)
        {
            Gravity = GravityFlags.Center
        };

        root.AddView(card, cardParams);

        WindowManagerTypes type =
            Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone;

        layoutParams = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent,
            type,
            WindowManagerFlags.NotFocusable |
            WindowManagerFlags.KeepScreenOn |
            WindowManagerFlags.ShowWhenLocked |
            WindowManagerFlags.TurnScreenOn,
            Android.Graphics.Format.Translucent)
        {
            Gravity = GravityFlags.Center
        };

        overlayView = root;

        try
        {
            windowManager?.AddView(overlayView, layoutParams);
        }
        catch (WindowManagerBadTokenException)
        {
            AndroidReminderNotificationService.ShowPermissionRequiredNotification(
                this,
                reminder);
            StopSelf();
        }
        catch (Java.Lang.SecurityException)
        {
            AndroidReminderNotificationService.ShowPermissionRequiredNotification(
                this,
                reminder);
            StopSelf();
        }
    }

    internal void DismissReminderOverlay(int targetReminderId)
    {
        pendingOverlays = new Queue<PendingOverlay>(
            pendingOverlays.Where(
                overlay => overlay.Reminder.Id != targetReminderId));

        if (overlayView is not null &&
            displayedReminderId == targetReminderId)
        {
            RemoveOverlay();
            ShowNextOverlay();
        }
        else if (overlayView is null && pendingOverlays.Count == 0)
        {
            StopSelf();
        }
    }

    private void ShowNextOverlay()
    {
        if (pendingOverlays.Count == 0)
        {
            StopSelf();
            return;
        }

        PendingOverlay nextOverlay = pendingOverlays.Dequeue();
        AddOverlay(nextOverlay.Reminder, nextOverlay.Settings);
    }

    private void TriggerAlert(ReminderItem reminder)
    {
        StartAlarmSignal();
        RegisterUnlockReceiver();

        Notification notification = new NotificationCompat.Builder(this, AndroidReminderNotificationService.AlarmChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle(reminder.Text)
            .SetContentText(
                ReminderDisplayFormatter.GetDisplayText(
                    reminder.DisplayStart,
                    reminder.DisplayEnd))
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(reminder.Text))
            .SetPriority(NotificationCompat.PriorityMax)
            .SetCategory(NotificationCompat.CategoryAlarm)
            .SetVisibility(NotificationCompat.VisibilityPublic)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetSilent(true)
            .SetOnlyAlertOnce(true)
            .Build();

        NotificationManagerCompat manager = NotificationManagerCompat.From(this);
        if (!manager.AreNotificationsEnabled())
        {
            return;
        }

        manager.Notify(AndroidReminderNotificationService.AlarmNotificationIdOffset + reminder.Id, notification);
    }

    private void StartAlarmSignal()
    {
        StopAlarmSignal();
        RegisterUnlockReceiver();

        Android.Net.Uri? alarmSound =
    RingtoneManager.GetDefaultUri(RingtoneType.Alarm)
    ?? RingtoneManager.GetDefaultUri(RingtoneType.Ringtone)
    ?? RingtoneManager.GetDefaultUri(RingtoneType.Notification);

        if (alarmSound is not null)
        {
            alarmPlayer = new MediaPlayer();
            alarmPlayer.SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Alarm)
                .SetContentType(AudioContentType.Sonification)
                .Build());
            alarmPlayer.SetDataSource(this, alarmSound);
            alarmPlayer.Looping = true;
            alarmPlayer.SetVolume(1f, 1f);
            alarmPlayer.Prepare();
            alarmPlayer.Start();
        }

        vibrator = Build.VERSION.SdkInt >= BuildVersionCodes.S
            ? ((VibratorManager)GetSystemService(VibratorManagerService)!).DefaultVibrator
            : (Vibrator?)GetSystemService(VibratorService);

        long[] pattern = [0, 800, 400, 800, 400, 1200];
        if (vibrator is null || !vibrator.HasVibrator)
        {
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            vibrator.Vibrate(VibrationEffect.CreateWaveform(pattern, 0));
        }
        else
        {
#pragma warning disable CS0618
            vibrator.Vibrate(pattern, 0);
#pragma warning restore CS0618
        }
    }

    private void StopAlarmSignal()
    {
        UnregisterUnlockReceiver();

        if (alarmPlayer is not null)
        {
            if (alarmPlayer.IsPlaying)
            {
                alarmPlayer.Stop();
            }

            alarmPlayer.Release();
            alarmPlayer.Dispose();
            alarmPlayer = null;
        }

        vibrator?.Cancel();
        vibrator = null;
    }
    private void RegisterUnlockReceiver()
    {
        if (isUnlockReceiverRegistered)
        {
            return;
        }

        unlockReceiver = new UserPresentReceiver(StopAlarmAfterUnlock, IsDeviceLocked);

        IntentFilter filter = new(Intent.ActionUserPresent);
        filter.AddAction(Intent.ActionScreenOff);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RegisterReceiver(unlockReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
#pragma warning disable CS0618
            RegisterReceiver(unlockReceiver, filter);
#pragma warning restore CS0618
        }

        isUnlockReceiverRegistered = true;
    }

    private bool IsDeviceLocked()
    {
        KeyguardManager? keyguardManager = GetSystemService(KeyguardService).JavaCast<KeyguardManager>();
        return keyguardManager?.IsKeyguardLocked == true;
    }

    private void StopAlarmAfterUnlock()
    {
        StopAlarmSignal();
        NotificationManagerCompat.From(this).Cancel(AndroidReminderNotificationService.AlarmNotificationIdOffset + reminderId);
        if (overlayView is null)
        {
            StopSelf();
        }
    }

    private void UnregisterUnlockReceiver()
    {
        if (!isUnlockReceiverRegistered || unlockReceiver is null)
        {
            return;
        }

        UnregisterReceiver(unlockReceiver);
        unlockReceiver.Dispose();
        unlockReceiver = null;
        isUnlockReceiverRegistered = false;
    }

    private sealed class UserPresentReceiver(Action onUserPresent, Func<bool> isDeviceLocked) : BroadcastReceiver
    {
        private bool observedLockedState = isDeviceLocked();

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == Intent.ActionScreenOff)
            {
                observedLockedState |= isDeviceLocked();
                return;
            }

            if (intent?.Action == Intent.ActionUserPresent && observedLockedState && !isDeviceLocked())
            {
                onUserPresent();
            }
        }
    }

    private void RemoveOverlay()
    {
        if (overlayView is not null && windowManager is not null)
        {
            windowManager.RemoveView(overlayView);
        }
        overlayView = null;
        displayedReminderId = 0;
        StopAlarmSignal();
    }
}
