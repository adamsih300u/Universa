using System;
using System.Diagnostics;

namespace Universa.Desktop.Core.Logging
{
    public static class Log
    {
        public static void Information(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public static void Warning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public static void Error(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public static void Error(Exception ex, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] Exception: {ex}");
        }

        public static void Debug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }
    }
} 