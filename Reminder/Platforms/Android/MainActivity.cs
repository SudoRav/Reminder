using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using Android.Views;

namespace Reminder
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, WindowSoftInputMode = SoftInput.AdjustResize)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window?.SetSoftInputMode(SoftInput.AdjustResize);
            HandleIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Intent = intent;
            HandleIntent(intent);
        }

        protected override void OnResume()
        {
            base.OnResume();
            StopActiveReminderAlarm();
        }

        private void StopActiveReminderAlarm()
        {
            Intent serviceIntent = new(this, typeof(ReminderOverlayService));
            serviceIntent.SetAction(AndroidReminderNotificationService.StopAlarmAction);
            StartService(serviceIntent);
        }

        private static void HandleIntent(Intent? intent)
        {
            if (intent?.Action != AndroidReminderNotificationService.OpenEditorAction)
            {
                return;
            }

            int reminderId = intent.GetIntExtra(AndroidReminderNotificationService.ReminderIdExtra, 0);
            if (reminderId != 0)
            {
                MainThread.BeginInvokeOnMainThread(() => AndroidReminderNotificationService.NotifyReminderEditorRequested(reminderId));
            }
        }
    }
}
