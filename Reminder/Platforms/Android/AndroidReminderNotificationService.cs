#if ANDROID
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using System.Text.Json;

namespace Reminder;

public sealed class AndroidReminderNotificationService : IReminderNotificationService
{
    private const string ChannelId = "persistent_reminders";
    internal const string AlarmChannelId = "scheduled_reminder_alarms";
    private const int NotificationPermissionRequestCode = 1001;
    private const string AlarmAction = "com.companyname.reminder.SHOW_OVERLAY_REMINDER";
    internal const string CompleteAction = "com.companyname.reminder.COMPLETE_REMINDER";
    internal const string OpenEditorAction = "com.companyname.reminder.OPEN_REMINDER_EDITOR";
    internal const string ReminderIdExtra = "reminder_id";
    internal const string NotificationTimeTicksExtra = "notification_time_ticks";

    private readonly Context context;
    private readonly NotificationManager notificationManager;
    private readonly AlarmManager alarmManager;

    public static event Action<int>? ReminderCompleted;
    public static event Action<int>? ReminderEditorRequested;
    public static event Action<int, DateTime>? NotificationTimeTriggered;

    internal static void NotifyReminderCompleted(int reminderId) => ReminderCompleted?.Invoke(reminderId);
    internal static void NotifyReminderEditorRequested(int reminderId) => ReminderEditorRequested?.Invoke(reminderId);
    private static void NotifyNotificationTimeTriggered(int reminderId, DateTime notificationTime) => NotificationTimeTriggered?.Invoke(reminderId, notificationTime);

    public AndroidReminderNotificationService()
    {
        context = Platform.AppContext;
        notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        CreateNotificationChannel();
        CreateAlarmNotificationChannel();
    }

    public async Task ShowAsync(ReminderItem reminder)
    {
        await EnsureOverlayPermissionAsync();
        ScheduleNotificationTimeAlarms(reminder);

        if (!await EnsureNotificationPermissionAsync())
        {
            return;
        }

        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        PendingIntent? pendingIntent = PendingIntent.GetActivity(context, reminder.Id, CreateOpenEditorIntent(reminder.Id), flags);
        PendingIntent? completePendingIntent = PendingIntent.GetBroadcast(context, reminder.Id, CreateCompleteIntent(reminder.Id), flags);

        Notification notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle(ReminderDisplayFormatter.GetDisplayText(reminder.DisplayStart, reminder.DisplayEnd))
            .SetContentText(reminder.Text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(reminder.Text))
            .SetContentIntent(pendingIntent)
            .AddAction(Resource.Drawable.notification_icon, "Завершить", completePendingIntent)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetPriority(NotificationCompat.PriorityDefault)
            .Build();

        notificationManager.Notify(reminder.Id, notification);
    }

    public async Task ScheduleAsync(ReminderItem reminder)
    {
        await EnsureOverlayPermissionAsync();
        await EnsureNotificationPermissionAsync();
        ScheduleNotificationTimeAlarms(reminder);
    }

    public void Cancel(int reminderId)
    {
        notificationManager.Cancel(reminderId);
        CancelNotificationTimeAlarms(reminderId);
        DismissOverlay(context, reminderId);
    }

    internal static ReminderItem? LoadReminder(int reminderId)
    {
        string json = Preferences.Default.Get("reminders", "[]");
        try
        {
            return JsonSerializer.Deserialize<List<ReminderItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?.FirstOrDefault(reminder => reminder.Id == reminderId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static Intent CreateOpenEditorIntent(int reminderId)
    {
        Intent intent = Platform.AppContext.PackageManager?.GetLaunchIntentForPackage(Platform.AppContext.PackageName!)
            ?? new Intent(Platform.AppContext, typeof(MainActivity));
        intent.SetAction(OpenEditorAction);
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
        intent.PutExtra(ReminderIdExtra, reminderId);
        return intent;
    }

    private Intent CreateCompleteIntent(int reminderId)
    {
        Intent completeIntent = new(context, typeof(CompleteReminderReceiver));
        completeIntent.SetAction(CompleteAction);
        completeIntent.PutExtra(ReminderIdExtra, reminderId);
        return completeIntent;
    }

    private void ScheduleNotificationTimeAlarms(ReminderItem reminder)
    {
        CancelNotificationTimeAlarms(reminder.Id);

        DateTime now = DateTime.Now;
        foreach (DateTime notificationTime in reminder.NotificationTimes.Where(time => time > now).Distinct())
        {
            PendingIntent? pendingIntent = CreateNotificationTimePendingIntent(reminder.Id, notificationTime);
            long triggerAtMillis = new DateTimeOffset(notificationTime).ToUnixTimeMilliseconds();
            ScheduleNotificationTimeAlarm(triggerAtMillis, pendingIntent);
        }
    }

    private void ScheduleNotificationTimeAlarm(long triggerAtMillis, PendingIntent? pendingIntent)
    {
        if (pendingIntent is null)
        {
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S && alarmManager.CanScheduleExactAlarms())
        {
            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
            return;
        }

        if (Build.VERSION.SdkInt < BuildVersionCodes.S)
        {
            try
            {
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
                return;
            }
            catch (Java.Lang.SecurityException)
            {
                // Fall back to an inexact alarm when exact alarms are blocked by the device policy.
            }
        }

        alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
    }

    private void CancelNotificationTimeAlarms(int reminderId)
    {
        ReminderItem? reminder = LoadReminder(reminderId);
        if (reminder is null)
        {
            return;
        }

        foreach (DateTime notificationTime in reminder.NotificationTimes.Distinct())
        {
            PendingIntent? pendingIntent = CreateNotificationTimePendingIntent(reminderId, notificationTime);
            if (pendingIntent is not null)
            {
                alarmManager.Cancel(pendingIntent);
                pendingIntent.Cancel();
            }
        }
    }

    private PendingIntent? CreateNotificationTimePendingIntent(int reminderId, DateTime notificationTime)
    {
        Intent intent = new(context, typeof(OverlayReminderReceiver));
        intent.SetAction(AlarmAction);
        intent.PutExtra(ReminderIdExtra, reminderId);
        intent.PutExtra(NotificationTimeTicksExtra, notificationTime.Ticks);
        return PendingIntent.GetBroadcast(context, GetNotificationTimeRequestCode(reminderId, notificationTime), intent, GetImmutableFlags());
    }

    private static int GetNotificationTimeRequestCode(int reminderId, DateTime notificationTime)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + reminderId;
            hash = (hash * 31) + notificationTime.Ticks.GetHashCode();
            return hash;
        }
    }

    private static PendingIntentFlags GetImmutableFlags()
    {
        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }
        return flags;
    }

    internal static void ShowOverlay(Context context, ReminderItem reminder, DateTime? notificationTime)
    {
        if (!CanDrawOverlay(context))
        {
            ShowPermissionRequiredNotification(context, reminder);
            return;
        }

        ReminderItem overlayReminder = notificationTime.HasValue
            ? RemoveNotificationTime(reminder.Id, notificationTime.Value) ?? reminder
            : reminder;

        Intent serviceIntent = new(context, typeof(ReminderOverlayService));
        serviceIntent.PutExtra(ReminderIdExtra, overlayReminder.Id);
        ContextCompat.StartForegroundService(context, serviceIntent);
    }


    private static ReminderItem? RemoveNotificationTime(int reminderId, DateTime notificationTime)
    {
        string json = Preferences.Default.Get("reminders", "[]");
        List<ReminderItem> reminders;
        try
        {
            reminders = JsonSerializer.Deserialize<List<ReminderItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }

        ReminderItem? reminder = reminders.FirstOrDefault(reminder => reminder.Id == reminderId);
        if (reminder is null)
        {
            return null;
        }

        int removedCount = reminder.NotificationTimes.RemoveAll(time => time == notificationTime);
        if (removedCount == 0)
        {
            return reminder;
        }

        Preferences.Default.Set("reminders", JsonSerializer.Serialize(reminders, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        MainThread.BeginInvokeOnMainThread(() => NotifyNotificationTimeTriggered(reminderId, notificationTime));
        return reminder;
    }

    internal static void DismissOverlay(Context context, int reminderId)
    {
        Intent serviceIntent = new(context, typeof(ReminderOverlayService));
        serviceIntent.PutExtra(ReminderIdExtra, reminderId);
        context.StopService(serviceIntent);
    }

    private static bool CanDrawOverlay(Context context) =>
        Build.VERSION.SdkInt < BuildVersionCodes.M || Settings.CanDrawOverlays(context);

    private static Task EnsureOverlayPermissionAsync()
    {
        if (CanDrawOverlay(Platform.AppContext))
        {
            return Task.CompletedTask;
        }

        Intent settingsIntent = new(Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse($"package:{Platform.AppContext.PackageName}"));
        settingsIntent.SetFlags(ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(settingsIntent);
        return Task.CompletedTask;
    }

    internal static void ShowPermissionRequiredNotification(Context context, ReminderItem reminder)
    {
        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        Intent settingsIntent = new(Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse($"package:{context.PackageName}"));
        PendingIntent? settingsPendingIntent = PendingIntent.GetActivity(context, reminder.Id, settingsIntent, flags);
        Notification notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle("Разрешите показ поверх окон")
            .SetContentText(reminder.Text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(reminder.Text))
            .SetContentIntent(settingsPendingIntent)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityHigh)
            .Build();

        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Notify(20_000 + reminder.Id, notification);
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var channel = new NotificationChannel(
            ChannelId,
            "Постоянные напоминания",
            NotificationImportance.High)
        {
            Description = "Липкие уведомления"
        };

        channel.EnableVibration(true);
        channel.SetVibrationPattern(new long[] { 0, 300, 150, 300 });
        channel.SetShowBadge(true);
        channel.EnableVibration(true);
        notificationManager.CreateNotificationChannel(channel);
    }

    private void CreateAlarmNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        Uri alarmSound = RingtoneManager.GetDefaultUri(RingtoneType.Alarm)
            ?? RingtoneManager.GetDefaultUri(RingtoneType.Ringtone)
            ?? RingtoneManager.GetDefaultUri(RingtoneType.Notification);

        AudioAttributes audioAttributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Alarm)
            .SetContentType(AudioContentType.Sonification)
            .Build();

        var channel = new NotificationChannel(
            AlarmChannelId,
            "Будильники напоминаний",
            NotificationImportance.Max)
        {
            Description = "Громкие уведомления в назначенное время"
        };

        channel.EnableVibration(true);
        channel.SetVibrationPattern([0, 800, 400, 800, 400, 1200]);
        channel.SetSound(alarmSound, audioAttributes);
        channel.LockscreenVisibility = NotificationVisibility.Public;
        notificationManager.CreateNotificationChannel(channel);
    }

    private static async Task<bool> EnsureNotificationPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return true;
        if (ContextCompat.CheckSelfPermission(Platform.AppContext, Manifest.Permission.PostNotifications) == Permission.Granted) return true;
        PermissionStatus status = await Permissions.RequestAsync<PostNotificationsPermission>();
        return status == PermissionStatus.Granted;
    }

    private sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [(Manifest.Permission.PostNotifications, true)];
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class OverlayReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        int reminderId = intent?.GetIntExtra(AndroidReminderNotificationService.ReminderIdExtra, 0) ?? 0;
        ReminderItem? reminder = AndroidReminderNotificationService.LoadReminder(reminderId);
        if (reminder is not null)
        {
            long notificationTimeTicks = intent?.GetLongExtra(AndroidReminderNotificationService.NotificationTimeTicksExtra, 0L) ?? 0L;
            DateTime? notificationTime = notificationTimeTicks == 0L ? null : new DateTime(notificationTimeTicks);
            AndroidReminderNotificationService.ShowOverlay(context, reminder, notificationTime);
        }
    }
}

[Service(Enabled = true, Exported = false)]
public sealed class ReminderOverlayService : Service
{
    private WindowManagerLayoutParams? layoutParams;
    private IWindowManager? windowManager;
    private Android.Views.View? overlayView;
    private MediaPlayer? alarmPlayer;
    private Vibrator? vibrator;
    private int reminderId;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        reminderId = intent?.GetIntExtra(AndroidReminderNotificationService.ReminderIdExtra, 0) ?? 0;
        if (intent?.Action == AndroidReminderNotificationService.CompleteAction)
        {
            RemoveOverlay();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        ReminderItem? reminder = AndroidReminderNotificationService.LoadReminder(reminderId);
        if (reminder is null)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        StartForeground(10_000 + reminder.Id, BuildForegroundNotification(reminder));
        AddOverlay(reminder);
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        RemoveOverlay();
        StopAlarmSignal();
        base.OnDestroy();
    }

    private Notification BuildForegroundNotification(ReminderItem reminder) => new NotificationCompat.Builder(this, "persistent_reminders")
        .SetSmallIcon(Resource.Drawable.notification_icon)
        .SetContentTitle("Напоминание поверх приложений")
        .SetContentText(reminder.Text)
        .SetOngoing(true)
        .Build();

    private void AddOverlay(ReminderItem reminder)
    {
        RemoveOverlay();
        TriggerAlert(reminder);

        windowManager = GetSystemService(WindowService).JavaCast<IWindowManager>();

        var metrics = Resources.DisplayMetrics;

        int screenWidth = metrics.WidthPixels;
        int screenHeight = metrics.HeightPixels;

        // Размер карточки внутри полноэкранного прозрачного слоя.
        int overlayWidth = (int)(screenWidth * 0.8f);
        int overlayHeight = (int)(screenHeight * 0.25f);

        var root = new Android.Widget.FrameLayout(this);
        root.SetBackgroundColor(Android.Graphics.Color.Transparent);
        root.Clickable = true;
        //root.Click += (_, _) =>
        //{
        //    RemoveOverlay();
        //    StopSelf();
        //};

        var card = new Android.Widget.LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Clickable = true
        };

        card.SetBackgroundColor(Android.Graphics.Color.White);
        card.SetPadding(40, 24, 40, 40);

        var header = new Android.Widget.LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };

        var title = new Android.Widget.TextView(this)
        {
            Text = "Напоминание",
            TextSize = 16
        };
        title.SetTextColor(Android.Graphics.Color.Black);

        header.AddView(title, new Android.Widget.LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var closeButton = new Android.Widget.TextView(this)
        {
            Text = "✕",
            TextSize = 24,
            Gravity = GravityFlags.Center
        };
        closeButton.SetTextColor(Android.Graphics.Color.Black);
        closeButton.SetPadding(24, 0, 0, 0);
        closeButton.Click += (_, _) =>
        {
            RemoveOverlay();
            StopSelf();
        };

        header.AddView(closeButton, new Android.Widget.LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        card.AddView(header, new Android.Widget.LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var textView = new Android.Widget.TextView(this)
        {
            Text = reminder.Text,
            TextSize = 18
        };

        textView.SetTextColor(Android.Graphics.Color.Black);

        var scrollView = new Android.Widget.ScrollView(this);
        scrollView.AddView(textView);

        var scrollParams = new Android.Widget.LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f);

        card.AddView(scrollView, scrollParams);

        var button = new Android.Widget.Button(this)
        {
            Text = "Завершить"
        };

        button.Click += (_, _) =>
        {
            SendBroadcast(
                new Android.Content.Intent(this, typeof(CompleteReminderReceiver))
                    .SetAction(AndroidReminderNotificationService.CompleteAction)
                    .PutExtra(AndroidReminderNotificationService.ReminderIdExtra, reminder.Id));

            RemoveOverlay();
            StopSelf();
        };

        var buttonParams = new Android.Widget.LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = 24
        };

        card.AddView(button, buttonParams);

        card.Click += (_, _) =>
        {
            StartActivity(AndroidReminderNotificationService.CreateOpenEditorIntent(reminder.Id));
            RemoveOverlay();
            StopSelf();
        };

        var cardParams = new Android.Widget.FrameLayout.LayoutParams(overlayWidth, overlayHeight)
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
            WindowManagerFlags.TurnScreenOn |
            WindowManagerFlags.DismissKeyguard,
            Format.Translucent)
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
            AndroidReminderNotificationService.ShowPermissionRequiredNotification(this, reminder);
            StopSelf();
        }
        catch (Java.Lang.SecurityException)
        {
            AndroidReminderNotificationService.ShowPermissionRequiredNotification(this, reminder);
            StopSelf();
        }
    }

    private void TriggerAlert(ReminderItem reminder)
    {
        StartAlarmSignal();

        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            flags |= PendingIntentFlags.Immutable;

        PendingIntent? pendingIntent = PendingIntent.GetActivity(
            this,
            reminder.Id,
            AndroidReminderNotificationService.CreateOpenEditorIntent(reminder.Id),
            flags);

        Notification notification = new NotificationCompat.Builder(this, AndroidReminderNotificationService.AlarmChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle("Напоминание")
            .SetContentText(reminder.Text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(reminder.Text))
            .SetPriority(NotificationCompat.PriorityMax)
            .SetCategory(NotificationCompat.CategoryAlarm)
            .SetVisibility(NotificationCompat.VisibilityPublic)
            .SetFullScreenIntent(pendingIntent, true)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetDefaults((int)NotificationDefaults.Sound | (int)NotificationDefaults.Vibrate | (int)NotificationDefaults.Lights)
            .SetContentIntent(pendingIntent)
            .Build();

        NotificationManagerCompat manager = NotificationManagerCompat.From(this);
        if (!manager.AreNotificationsEnabled())
        {
            return;
        }

        int notificationId = 900000 + reminder.Id;

        manager.Notify(notificationId, notification);
    }

    private void StartAlarmSignal()
    {
        StopAlarmSignal();

        Uri alarmSound = RingtoneManager.GetDefaultUri(RingtoneType.Alarm)
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

    private void RemoveOverlay()
    {
        if (overlayView is not null && windowManager is not null)
        {
            windowManager.RemoveView(overlayView);
        }
        overlayView = null;
        StopAlarmSignal();
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class CompleteReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != AndroidReminderNotificationService.CompleteAction) return;
        int reminderId = intent.GetIntExtra(AndroidReminderNotificationService.ReminderIdExtra, 0);
        if (reminderId == 0) return;

        string json = Preferences.Default.Get("reminders", "[]");
        List<ReminderItem> reminders;
        try
        {
            reminders = JsonSerializer.Deserialize<List<ReminderItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            reminders = [];
        }

        reminders.RemoveAll(reminder => reminder.Id == reminderId);
        Preferences.Default.Set("reminders", JsonSerializer.Serialize(reminders, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(reminderId);
        AndroidReminderNotificationService.DismissOverlay(context, reminderId);
        MainThread.BeginInvokeOnMainThread(() => AndroidReminderNotificationService.NotifyReminderCompleted(reminderId));
    }
}
#endif
