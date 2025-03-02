using System;

namespace Universa.Desktop
{
    public class MediaManager
    {
        public event EventHandler<MediaButtonEventArgs> ButtonPressed;

        public MediaManager()
        {
            // Register global hotkeys for media control
            // This is a placeholder for now - we'll implement actual hotkey handling later
        }

        public void SimulateButtonPress(MediaButton button)
        {
            ButtonPressed?.Invoke(this, new MediaButtonEventArgs(button));
        }
    }

    public enum MediaButton
    {
        None,
        Play,
        Pause,
        Next,
        Previous
    }

    public class MediaButtonEventArgs : EventArgs
    {
        public MediaButton Button { get; }

        public MediaButtonEventArgs(MediaButton button)
        {
            Button = button;
        }
    }
} 