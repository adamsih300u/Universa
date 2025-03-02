using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Universa.Desktop.Core.Theme
{
    public static class ThemeManager
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void SetDarkMode()
        {
            foreach (Window window in Application.Current.Windows)
            {
                SetWindowTheme(window, true);
            }
        }

        public static void SetLightMode()
        {
            foreach (Window window in Application.Current.Windows)
            {
                SetWindowTheme(window, false);
            }
        }

        public static void SetWindowTheme(Window window, bool isDarkMode)
        {
            if (window.IsLoaded)
            {
                SetWindowThemeCore(window, isDarkMode);
            }
            else
            {
                window.Loaded += (s, e) => SetWindowThemeCore(window, isDarkMode);
            }
        }

        private static void SetWindowThemeCore(Window window, bool isDarkMode)
        {
            var windowHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (windowHandle == IntPtr.Zero)
                return;

            int attribute = DWMWA_USE_IMMERSIVE_DARK_MODE;
            int useImmersiveDarkMode = isDarkMode ? 1 : 0;
            
            if (DwmSetWindowAttribute(windowHandle, attribute, ref useImmersiveDarkMode, sizeof(int)) != 0)
            {
                // Try the older attribute if the newer one fails
                attribute = DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
                DwmSetWindowAttribute(windowHandle, attribute, ref useImmersiveDarkMode, sizeof(int));
            }
        }
    }
} 