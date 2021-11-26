using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.LocalBroadcastManager.Content;
using System;
using System.Threading.Tasks;
using Xamarin.Essentials;
using static Xamarin.Essentials.Permissions;
using ALocation = Android.Locations.Location;

namespace WhileInUseLocation
{
    /// <summary>
    /// This app allows a user to receive location updates without the background permission even when
    /// the app isn't in focus. This is the preferred approach for Android.
    ///
    /// It does this by creating a foreground service (tied to a Notification) when the
    ///  user navigates away from the app. Because of this, it only needs foreground or "while in use"
    ///  location permissions. That is, there is no need to ask for location in the background (which
    ///  requires additional permissions in the manifest).
    ///
    ///  Note: Users have the following options in Android 11+ regarding location:
    ///
    ///  * Allow all the time
    ///  * Allow while app is in use, i.e., while app is in foreground (new in Android 10)
    ///  * Allow one time use(new in Android 11)
    ///  * Not allow location at all
    ///
    /// It is generally recommended you only request "while in use" location permissions(location only
    /// needed in the foreground), e.g., fine and coarse.If your app has an approved use case for
    /// using location in the background, request that permission in context and separately from
    /// fine/coarse location requests.In addition, if the user denies the request or only allows
    /// "while-in-use", handle it gracefully.To see an example of background location, please review
    /// {@link https://github.com/android/location-samples/tree/master/LocationUpdatesBackgroundKotlin}.
    ///
    /// Android 10 and higher also now requires developers to specify foreground service type in the
    /// manifest(in this case, "location").
    ///
    /// For the feature that requires location in the foreground, this sample uses a long-running bound
    /// and started service for location updates. The service is aware of foreground status of this
    /// activity, which is the only bound client in this sample.
    ///
    /// While getting location in the foreground, if the activity ceases to be in the foreground (user
    /// navigates away from the app), the service promotes itself to a foreground service and continues
    /// receiving location updates.
    ///
    /// When the activity comes back to the foreground, the foreground service stops, and the
    /// notification associated with that foreground service is removed.
    ///
    /// While the foreground service notification is displayed, the user has the option to launch the
    /// activity from the notification. The user can also remove location updates directly from the
    /// notification. This dismisses the notification and stops the service.
    /// </summary>
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        private bool foregroundOnlyLocationServiceBound = false;
        // Provides location updates for while-in-use feature.
        private ForegroundOnlyBroadcastReceiver foregroundOnlyBroadcastReceiver;
        
        private ISharedPreferences sharedPreferences;
        
        private ForegroundOnlyLocationService? foregroundOnlyLocationService = null;
        
        private Button foregroundOnlyLocationButton;

        private TextView outputTextView;

        private readonly IServiceConnection foregroundOnlyServiceConnection;

        public MainActivity()
        {
            foregroundOnlyServiceConnection = new ServiceConnectionImpl(this);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            foregroundOnlyBroadcastReceiver = new ForegroundOnlyBroadcastReceiver(this);

            sharedPreferences =
                GetSharedPreferences(GetString(Resource.String.preference_file_key), FileCreationMode.Private);

            foregroundOnlyLocationButton = FindViewById<Button>(Resource.Id.foreground_only_location_button);
            outputTextView = FindViewById<TextView>(Resource.Id.output_text_view);

            foregroundOnlyLocationButton.Click += IniciarColetaDeLocalizacao;

        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        protected override void OnStart()
        {
            base.OnStart();

            UpdateButtonState(
                sharedPreferences.GetBoolean(SharedPreferenceUtil.KEY_FOREGROUND_ENABLED, false)
            );

            sharedPreferences.RegisterOnSharedPreferenceChangeListener(this);

            var serviceIntent = new Intent(this, typeof(ForegroundOnlyLocationService));
            BindService(serviceIntent, foregroundOnlyServiceConnection, Bind.AutoCreate);
        }

        protected override void OnResume()
        {
            base.OnResume();
            LocalBroadcastManager.GetInstance(this).RegisterReceiver(
                foregroundOnlyBroadcastReceiver,
                new IntentFilter(
                    ForegroundOnlyLocationService.ACTION_FOREGROUND_ONLY_LOCATION_BROADCAST)
            );
        }

        protected override void OnPause()
        {
            LocalBroadcastManager.GetInstance(this).UnregisterReceiver(
                foregroundOnlyBroadcastReceiver
            );
            base.OnPause();
        }

        protected override void OnStop()
        {
            if (foregroundOnlyLocationServiceBound)
            {
                UnbindService(foregroundOnlyServiceConnection);
                foregroundOnlyLocationServiceBound = false;
            }
            sharedPreferences.UnregisterOnSharedPreferenceChangeListener(this);
            base.OnStop();
        }

        protected override void OnDestroy()
        {
            foregroundOnlyLocationService?.StopLocationService();
            base.OnDestroy();
        }

        async Task<bool> CheckAndRequestPermission<T>() where T : BasePermission, new()
        {
            var permissionStatus = await CheckStatusAsync<T>();

            if (permissionStatus == PermissionStatus.Granted) return true;

            permissionStatus = await RequestAsync<T>();
            return permissionStatus == PermissionStatus.Granted;
        }

        private async void IniciarColetaDeLocalizacao(object sender, EventArgs e)
        {
            var enabled = sharedPreferences.GetBoolean(
                    SharedPreferenceUtil.KEY_FOREGROUND_ENABLED, false);

            if (enabled)
            {
                foregroundOnlyLocationService?.UnsubscribeToLocationUpdates();
            }
            // TODO: Step 1.0, Review Permissions: Checks and requests if needed.
            else if (await CheckAndRequestPermission<LocationWhenInUse>())
            {
                foregroundOnlyLocationService?.SubscribeToLocationUpdates();
                if (foregroundOnlyLocationService is null)
                    Log.Debug("MainActivity", "Service Not Bound");
            }
        }

        private void UpdateButtonState(bool trackingLocation)
        {
            foregroundOnlyLocationButton.Text = trackingLocation
                ? GetString(Resource.String.stop_location_updates_button_text)
                : GetString(Resource.String.start_location_updates_button_text);
        }

        public void OnSharedPreferenceChanged(ISharedPreferences sharedPreferences, string key)
        {
            // Updates button states if new while in use location is added to SharedPreferences.
            if (key == SharedPreferenceUtil.KEY_FOREGROUND_ENABLED)
            {
                UpdateButtonState(sharedPreferences.GetBoolean(
                    SharedPreferenceUtil.KEY_FOREGROUND_ENABLED, false)
                );
            }
        }

        private void LogResultsToScreen(string output)
        {
            var outputWithPreviousLogs = $"{output}\n{outputTextView.Text}";
            outputTextView.Text = outputWithPreviousLogs;
        }

        /// <summary>
        /// Receiver for location broadcasts from [ForegroundOnlyLocationService].
        /// </summary>
        private class ForegroundOnlyBroadcastReceiver : BroadcastReceiver
        {
            private readonly MainActivity mainActivity;

            public ForegroundOnlyBroadcastReceiver(MainActivity mainActivity) => this.mainActivity = mainActivity;

            public override void OnReceive(Context context, Intent intent)
            {
                var location = (ALocation)intent
                    .GetParcelableExtra(ForegroundOnlyLocationService.EXTRA_LOCATION);

                if (location != null)
                {
                    mainActivity.LogResultsToScreen($"Foreground location: {location.ToText()}");
                }
            }
        }

        private class ServiceConnectionImpl : Java.Lang.Object, IServiceConnection
        {
            private readonly MainActivity mainActivity;

            public ServiceConnectionImpl(MainActivity baseColetaActivity) => this.mainActivity = baseColetaActivity;

            public void OnServiceConnected(ComponentName name, IBinder service)
            {
                var binder = service as ForegroundOnlyLocationService.LocalBinder;
                mainActivity.foregroundOnlyLocationService = binder.Service;
                mainActivity.foregroundOnlyLocationServiceBound = true;
            }

            public void OnServiceDisconnected(ComponentName name)
            {
                mainActivity.foregroundOnlyLocationService = null;
                mainActivity.foregroundOnlyLocationServiceBound = false;
            }
        }
    }
}