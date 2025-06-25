using System;
using System.Windows.Controls;
using Universa.Desktop.TTS;

namespace Universa.Desktop.Interfaces
{
    public interface IMarkdownTTSService
    {
        event EventHandler<bool> PlayingStateChanged;
        
        bool IsPlaying { get; }
        TTSClient TTSClient { get; set; }
        
        void Initialize(TextBox editor, ITextHighlighter textHighlighter);
        void StartTTS(string textToSpeak);
        void StopTTS();
        string GetTextToSpeak(string selectedText, string fullText);
        void OnPlaybackStarted();
        void OnPlaybackCompleted();
        void OnHighlightText(string text);
        void UpdateTabState(Action<bool> updateStateCallback);
        void Dispose();
    }
    
    public interface ITextHighlighter
    {
        void ClearHighlights();
        void HighlightText(string text, System.Windows.Media.Color color);
    }
} 