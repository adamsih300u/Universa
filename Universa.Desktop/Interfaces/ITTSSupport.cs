using System;
using Universa.Desktop.TTS;

namespace Universa.Desktop.Interfaces
{
    public interface ITTSSupport
    {
        string GetTextToSpeak();
        void StopTTS();
        TTSClient TTSClient { get; set; }
        bool IsPlaying { get; }
    }
} 