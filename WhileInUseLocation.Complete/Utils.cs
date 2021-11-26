using Android.Content;
using Android.Locations;

namespace WhileInUseLocation
{
    public static class LocationExtensions
    {
        public static string ToText(this Location location)
            => (location != null) ? $"({location.Latitude}, {location.Longitude})" : "Unknown location";
    }


    /// <summary>
    /// Provides access to SharedPreferences for location to Activities and Services.
    /// </summary>
    public static class SharedPreferenceUtil
    {

        public static string KEY_FOREGROUND_ENABLED = "tracking_foreground_location";

        /// <summary>
        /// True if requesting location updates, otherwise returns false.
        /// </summary>
        /// <param name="context">The [Context]</param>
        /// <returns>True if requesting location updates</returns>
        public static bool GetLocationTrackingPref(Context context) =>
            context.GetSharedPreferences(
                context.GetString(Resource.String.preference_file_key),
                FileCreationMode.Private)?
                .GetBoolean(KEY_FOREGROUND_ENABLED, false) ?? false;

        /// <summary>
        /// Stores the location updates state in SharedPreferences.
        /// </summary>
        /// <param name="context">The [Context]</param>
        /// <param name="requestingLocationUpdates">The location updates state.</param>
        public static void SaveLocationTrackingPref(Context context, bool requestingLocationUpdates)
        {
            var edit = context.GetSharedPreferences(
                 context.GetString(Resource.String.preference_file_key),
                 FileCreationMode.Private)?.Edit();
            edit?.PutBoolean(KEY_FOREGROUND_ENABLED, requestingLocationUpdates);
            edit?.Apply();
        }
    }
}