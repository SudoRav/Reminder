#if ANDROID
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Reminder;

public sealed class AndroidReminderNotificationService : IReminderNotificationService
{
    private const string ChannelId = "persistent_reminders";
    private const int NotificationPermissionRequestCode = 1001;

    private readonly Context context;
    private readonly NotificationManager notificationManager;

    public AndroidReminderNotificationService()
    {
        context = Platform.AppContext;
        notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        CreateNotificationChannel();
    }

    public async Task ShowAsync(ReminderItem reminder)
    {
        if (!await EnsureNotificationPermissionAsync())
        {
            return;
        }

        Intent launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!)
            ?? new Intent(context, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        PendingIntent? pendingIntent = PendingIntent.GetActivity(context, reminder.Id, launchIntent, flags);

        Notification notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle("Напоминание")
            .SetContentText(reminder.Text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(reminder.Text))
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetPriority(NotificationCompat.PriorityDefault)
            .Build();

        notificationManager.Notify(reminder.Id, notification);
    }

    public void Cancel(int reminderId)
    {
        notificationManager.Cancel(reminderId);
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var channel = new NotificationChannel(
            ChannelId,
            "Постоянные напоминания",
            NotificationImportance.Default)
        {
            Description = "Липкие уведомления для сохранённых напоминаний",
        };
        channel.SetShowBadge(true);
        notificationManager.CreateNotificationChannel(channel);
    }

    private static async Task<bool> EnsureNotificationPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return true;
        }

        if (ContextCompat.CheckSelfPermission(Platform.AppContext, Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return true;
        }

        PermissionStatus status = await Permissions.RequestAsync<PostNotificationsPermission>();
        return status == PermissionStatus.Granted;
    }

    private sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(Manifest.Permission.PostNotifications, true)];
    }
}
#endif
