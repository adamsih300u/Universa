using System;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Helper class for consistent time zone handling throughout the application.
    /// </summary>
    public static class TimeZoneHelper
    {
        /// <summary>
        /// Gets the current date and time in the local time zone.
        /// </summary>
        public static DateTime Now => DateTime.Now;

        /// <summary>
        /// Gets the current date and time in UTC.
        /// </summary>
        public static DateTime UtcNow => DateTime.UtcNow;

        /// <summary>
        /// Gets the local time zone information.
        /// </summary>
        public static TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

        /// <summary>
        /// Gets the local time zone display name.
        /// </summary>
        public static string LocalTimeZoneDisplayName => TimeZoneInfo.Local.DisplayName;

        /// <summary>
        /// Gets the local time zone ID.
        /// </summary>
        public static string LocalTimeZoneId => TimeZoneInfo.Local.Id;

        /// <summary>
        /// Gets the current UTC offset.
        /// </summary>
        public static TimeSpan CurrentUtcOffset => TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);

        /// <summary>
        /// Converts a UTC DateTime to local time.
        /// </summary>
        public static DateTime ToLocalTime(DateTime utcDateTime)
        {
            if (utcDateTime.Kind == DateTimeKind.Local)
                return utcDateTime;

            return TimeZoneInfo.ConvertTimeFromUtc(
                utcDateTime.Kind == DateTimeKind.Unspecified 
                    ? DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc) 
                    : utcDateTime, 
                TimeZoneInfo.Local);
        }

        /// <summary>
        /// Converts a local DateTime to UTC.
        /// </summary>
        public static DateTime ToUtcTime(DateTime localDateTime)
        {
            if (localDateTime.Kind == DateTimeKind.Utc)
                return localDateTime;

            return TimeZoneInfo.ConvertTimeToUtc(
                localDateTime.Kind == DateTimeKind.Unspecified 
                    ? DateTime.SpecifyKind(localDateTime, DateTimeKind.Local) 
                    : localDateTime);
        }

        /// <summary>
        /// Converts a Unix timestamp (milliseconds since epoch) to local DateTime.
        /// </summary>
        public static DateTime FromUnixTimeMilliseconds(long milliseconds)
        {
            return ToLocalTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
        }

        /// <summary>
        /// Converts a DateTime to Unix timestamp (milliseconds since epoch).
        /// </summary>
        public static long ToUnixTimeMilliseconds(DateTime dateTime)
        {
            return new DateTimeOffset(ToUtcTime(dateTime)).ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Gets a formatted string with the current date, time, and time zone information.
        /// </summary>
        public static string GetFormattedDateTimeWithTimeZone()
        {
            return $"{Now:yyyy-MM-dd HH:mm:ss} ({CurrentUtcOffset:hh\\:mm} from UTC)";
        }
    }
} 