/*
 * Copyright 2019 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#nullable enable
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Gms.Location;
using Android.Gms.Tasks;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Android.Util;
using AndroidX.Core.App;
using AndroidX.LocalBroadcastManager.Content;
using System;
using System.Security;
using LocationRequest = Android.Gms.Location.LocationRequest;

namespace WhileInUseLocation
{

    /// <summary>
    /// Service tracks location when requested and updates Activity via binding. If Activity is
    /// stopped / unbinds and tracking is enabled, the service promotes itself to a foreground service to
    /// insure location updates aren't interrupted.
    /// 
    /// For apps running in the background on O+ devices, location is computed much less than previous
    /// versions. Please reference documentation for details.
    /// </summary>
    [Service(ForegroundServiceType = ForegroundService.TypeLocation, Exported = true, Enabled = true)]
    internal class ForegroundOnlyLocationService : Service
    {

        private static readonly string TAG = "ForegroundOnlyLocationService";

        private static readonly string PACKAGE_NAME = ".example.android.whileinuselocation";

        internal static string ACTION_FOREGROUND_ONLY_LOCATION_BROADCAST =
            $"{PACKAGE_NAME}.action.FOREGROUND_ONLY_LOCATION_BROADCAST";

        internal static readonly string EXTRA_LOCATION = $"{PACKAGE_NAME}.extra.LOCATION";

        private static readonly string EXTRA_CANCEL_LOCATION_TRACKING_FROM_NOTIFICATION =
            $"{PACKAGE_NAME}.extra.CANCEL_LOCATION_TRACKING_FROM_NOTIFICATION";

        private static readonly int NOTIFICATION_ID = 12345678;

        private static readonly string NOTIFICATION_CHANNEL_ID = "while_in_use_channel_01";

        //Checks whether the bound activity has really gone away (foreground service with notification
        //created) or simply orientation change (no-op).
        private bool configurationChange = false;

        private bool serviceRunningInForeground = false;

        private readonly LocalBinder localBinder;

        private NotificationManager? notificationManager;

        // TODO: Step 1.1, Review variables (no changes).
        // FusedLocationProviderClient - Main class for receiving location updates.
        private FusedLocationProviderClient? fusedLocationProviderClient;

        // LocationRequest - Requirements for the location updates, i.e., how often you should receive
        // updates, the priority, etc.
        private LocationRequest? locationRequest;

        // LocationCallback - Called when FusedLocationProviderClient has a new Location.
        private LocationCallback? locationCallback;

        // Used only for local storage of the last known location. Usually, this would be saved to your
        // database, but because this is a simplified sample without a full database, we only need the
        // last location to create a Notification if the user navigates away from the app.
        private Location? currentLocation = null;

        public ForegroundOnlyLocationService()
        {
            localBinder = new LocalBinder(this);
        }

        public override void OnCreate()
        {
            Log.Debug(TAG, "onCreate()");

            notificationManager = GetSystemService(Context.NotificationService) as NotificationManager;

            // TODO: Step 1.2, Review the FusedLocationProviderClient.
            fusedLocationProviderClient = LocationServices.GetFusedLocationProviderClient(this);

            // TODO: Step 1.3, Create a LocationRequest.
            locationRequest = LocationRequest.Create()
            // Sets the desired intervar for active location updates. This intervar is inexact. You
            // may not receive updates at all if no location sources are available, or you may
            // receive them less frequently than requested. You may also receive updates more
            // frequently than requested if other applications are requesting location at a more
            // frequent interval.
            //
            // IMPORTANT NOTE: Apps running on Android 8.0 and higher devices (regardless of
            // targetSdkVersion) may receive updates less frequently than this intervar when the app
            // is no longer in the foreground.
            .SetInterval(TimeSpan.FromSeconds(60).Milliseconds)
            // Sets the fastest rate for active location updates. This intervar is exact, and your
            // application will never receive updates more frequently than this value.
            .SetFastestInterval(TimeSpan.FromSeconds(30).Milliseconds)
            // Sets the maximum time when batched location updates are delivered. Updates may be
            // delivered sooner than this interval.
            .SetMaxWaitTime(TimeSpan.FromMinutes(2).Milliseconds)
            .SetPriority(LocationRequest.PriorityHighAccuracy);


            // TODO: Step 1.4, Initialize the LocationCallback.
            locationCallback = new LocationCallbackImpl((LocationResult locationResult) =>
            {

                // Normally, you want to save a new location to a database. We are simplifying
                // things a bit and just saving it as a local variable, as we only need it again
                // if a Notification is created (when the user navigates away from app).
                currentLocation = locationResult.LastLocation;

                // Notify our Activity that a new location was added. Again, if this was a
                // production app, the Activity would be listening for changes to a database
                // with new locations, but we are simplifying things a bit to focus on just
                // learning the location side of things.
                var intent = new Intent(ACTION_FOREGROUND_ONLY_LOCATION_BROADCAST);
                intent.PutExtra(EXTRA_LOCATION, currentLocation);
                LocalBroadcastManager.GetInstance(ApplicationContext).SendBroadcast(intent);

                // Updates notification content if this service is running as a foreground
                // service.
                if (serviceRunningInForeground)
                {
                    notificationManager?.Notify(
                        NOTIFICATION_ID,
                        GenerateNotification(currentLocation));
                }
            });
        }

        [return: GeneratedEnum]
        public override StartCommandResult OnStartCommand(Intent? intent, [GeneratedEnum] StartCommandFlags flags, int startId)
        {

            Log.Debug(TAG, "onStartCommand()");

            var cancelLocationTrackingFromNotification =
                intent?.GetBooleanExtra(EXTRA_CANCEL_LOCATION_TRACKING_FROM_NOTIFICATION, false) ?? false;

            if (cancelLocationTrackingFromNotification)
            {
                UnsubscribeToLocationUpdates();
                StopSelf();
            }
            // Tells the system not to recreate the service after it's been killed.
            return StartCommandResult.NotSticky;
        }

        public override IBinder? OnBind(Intent? intent)
        {
            Log.Debug(TAG, "onBind()");

            // MainActivity (client) comes into foreground and binds to service, so the service can
            // become a background services.
            StopForeground(true);
            serviceRunningInForeground = false;
            configurationChange = false;
            return localBinder;
        }
        public override void OnRebind(Intent? intent)
        {

            Log.Debug(TAG, "onRebind()");

            // MainActivity (client) returns to the foreground and rebinds to service, so the service
            // can become a background services.
            StopForeground(true);
            serviceRunningInForeground = false;
            configurationChange = false;
            base.OnRebind(intent);
        }

        public override bool OnUnbind(Intent? intent)
        {

            Log.Debug(TAG, "onUnbind()");

            // MainActivity (client) leaves foreground, so service needs to become a foreground service
            // to maintain the 'while-in-use' label.
            // NOTE: If this method is called due to a configuration change in MainActivity,
            // we do nothing.
            if (!configurationChange && SharedPreferenceUtil.GetLocationTrackingPref(this))
            {
                Log.Debug(TAG, "Start foreground service");
                var notification = GenerateNotification(currentLocation);
                StartForeground(NOTIFICATION_ID, notification);
                serviceRunningInForeground = true;
            }

            // Ensures onRebind() is called if MainActivity (client) rebinds.
            return true;
        }

        public override void OnDestroy()
        {
            Log.Debug(TAG, "onDestroy()");
        }

        public override void OnConfigurationChanged(Configuration? newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            configurationChange = true;
        }

        public void SubscribeToLocationUpdates()
        {
            Log.Debug(TAG, "subscribeToLocationUpdates()");

            SharedPreferenceUtil.SaveLocationTrackingPref(this, true);

            // Binding to this service doesn't actually trigger onStartCommand(). That is needed to
            // ensure this Service can be promoted to a foreground service, i.e., the service needs to
            // be officially started (which we do here).
            StartService(new Intent(ApplicationContext, typeof(ForegroundOnlyLocationService)));

            try
            {
                // TODO: Step 1.5, Subscribe to location changes.
                fusedLocationProviderClient?.RequestLocationUpdates(
                    locationRequest, locationCallback, Looper.MainLooper);
            }
            catch (SecurityException unlikely)
            {
                SharedPreferenceUtil.SaveLocationTrackingPref(this, false);
                Log.Error(TAG, "Lost location permissions. Couldn't remove updates. $unlikely");
            }
        }

        public void UnsubscribeToLocationUpdates()
        {
            Log.Debug(TAG, "unsubscribeToLocationUpdates()");

            try
            {
                // TODO: Step 1.6, Unsubscribe to location changes.
                var removeTask = fusedLocationProviderClient?.RemoveLocationUpdates(locationCallback);
                removeTask?.AddOnCompleteListener(new CompleteListener((Task task) =>
                {
                    if (task.IsSuccessful)
                    {
                        Log.Debug(TAG, "Location Callback removed.");
                        StopSelf();
                    }
                    else
                    {
                        Log.Debug(TAG, "Failed to remove Location Callback.");
                    }

                }));

                SharedPreferenceUtil.SaveLocationTrackingPref(this, false);
            }
            catch (SecurityException unlikely)
            {
                SharedPreferenceUtil.SaveLocationTrackingPref(this, true);
                Log.Error(TAG, "Lost location permissions. Couldn't remove updates. $unlikely");
            }
        }

        public void StopLocationService()
        {
            UnsubscribeToLocationUpdates();
            StopSelf();
        }

        /// <summary>
        /// Generates a BIG_TEXT_STYLE Notification that represent latest location.
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        private Notification GenerateNotification(Location? location)
        {
            Log.Debug(TAG, "generateNotification()");

            // Main steps for building a BIG_TEXT_STYLE notification:
            //      0. Get data
            //      1. Create Notification Channel for O+
            //      2. Build the BIG_TEXT_STYLE
            //      3. Set up Intent / Pending Intent for notification
            //      4. Build and issue the notification

            // 0. Get data
            var mainNotificationText = location?.ToText() ?? "Sem Localização";
            var titleText = GetString(Resource.String.app_name);

            // 1. Create Notification Channel for O+ and beyond devices (26+).
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {

                var notificationChannel = new NotificationChannel(
                    NOTIFICATION_CHANNEL_ID, titleText, NotificationImportance.Default);
                notificationChannel.SetSound(null, null);
                // Adds NotificationChannel to system. Attempting to create an
                // existing notification channel with its original values performs
                // no operation, so it's safe to perform the below sequence.
                notificationManager?.CreateNotificationChannel(notificationChannel);
            }

            // 2. Build the BIG_TEXT_STYLE.
            var bigTextStyle = new NotificationCompat.BigTextStyle()
                .BigText(mainNotificationText)
                .SetBigContentTitle(titleText);

            // 3. Set up main Intent/Pending Intents for notification.
            var launchActivityIntent = new Intent(this, typeof(MainActivity));

            var cancelIntent = new Intent(this, typeof(ForegroundOnlyLocationService));
            cancelIntent.PutExtra(EXTRA_CANCEL_LOCATION_TRACKING_FROM_NOTIFICATION, true);

            var servicePendingIntent = PendingIntent.GetService(
                this, 0, cancelIntent, PendingIntentFlags.UpdateCurrent);

            var activityPendingIntent = PendingIntent.GetActivity(
                this, 0, launchActivityIntent, 0);

            // 4. Build and issue the notification.
            // Notification Channel Id is ignored for Android pre O (26).
            var notificationCompatBuilder = new
                    NotificationCompat.Builder(ApplicationContext, NOTIFICATION_CHANNEL_ID);

            return notificationCompatBuilder
                .SetStyle(bigTextStyle)
                .SetContentTitle(titleText)
                .SetContentText(mainNotificationText)
                .SetSmallIcon(Resource.Mipmap.ic_launcher)
                .SetDefaults(NotificationCompat.DefaultAll)
                .SetOngoing(true)
                .SetVisibility(NotificationCompat.VisibilityPublic)
                .AddAction(
                    Resource.Drawable.ic_launch, GetString(Resource.String.launch_activity),
                    activityPendingIntent
                )
                .AddAction(
                    Resource.Drawable.ic_cancel,
                    GetString(Resource.String.stop_location_updates_button_text),
                    servicePendingIntent
                )
                .Build();
        }

        /// <summary>
        /// Class used for the client Binder.  Since this service runs in the same process as its
        /// clients, we don't need to deal with IPC.
        /// </summary>
        public class LocalBinder : Binder
        {
            internal ForegroundOnlyLocationService Service { get; }

            public LocalBinder(ForegroundOnlyLocationService service) => Service = service;

        }
    }

    class CompleteListener : Java.Lang.Object, IOnCompleteListener
    {
        private readonly Action<Task> callBack;

        public CompleteListener(Action<Task> callBack) => this.callBack = callBack;

        public void OnComplete(Task task) => callBack?.Invoke(task);
    }

    public class LocationCallbackImpl : LocationCallback
    {
        private readonly Action<LocationResult> p;

        public LocationCallbackImpl(Action<LocationResult> p) => this.p = p;

        public override void OnLocationResult(LocationResult result)
        {
            base.OnLocationResult(result);
            p?.Invoke(result);
        }
    }
}