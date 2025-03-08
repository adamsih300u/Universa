using System;
using System.Diagnostics;
using Universa.Desktop.Helpers;

namespace Universa.Desktop.Core.Logging
{
    public static class Log
    {
        public static void Information(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] {TimeZoneHelper.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public static void Warning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] {TimeZoneHelper.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public static void Error(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {TimeZoneHelper.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public static void Error(Exception ex, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {TimeZoneHelper.Now:yyyy-MM-dd HH:mm:ss} - {message}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] Exception: {ex}");
        }

        public static void Debug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] {TimeZoneHelper.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }
    }
} 