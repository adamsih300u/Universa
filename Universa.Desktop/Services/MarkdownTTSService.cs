using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Universa.Desktop.Interfaces;
using Universa.Desktop.TTS;

namespace Universa.Desktop.Services
{
    public class MarkdownTTSService : IMarkdownTTSService, IDisposable
    {
        private TTSClient _ttsClient;
        private ITextHighlighter _textHighlighter;
        private TextBox _editor;
        private bool _isPlaying;
        private bool _disposed;

        public event EventHandler<bool> PlayingStateChanged;

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    PlayingStateChanged?.Invoke(this, value);
                }
            }
        }

        public TTSClient TTSClient
        {
            get => _ttsClient;
            set
            {
                if (_ttsClient != null)
                {
                    UnsubscribeTTSEvents();
                }
                _ttsClient = value;
                if (_ttsClient != null)
                {
                    SubscribeTTSEvents();
                }
            }
        }

        public void Initialize(TextBox editor, ITextHighlighter textHighlighter)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _textHighlighter = textHighlighter ?? throw new ArgumentNullException(nameof(textHighlighter));
        }

        public void StartTTS(string textToSpeak)
        {
            if (_ttsClient == null)
            {
                Debug.WriteLine("TTSClient is null, cannot start TTS");
                return;
            }

            if (string.IsNullOrWhiteSpace(textToSpeak))
            {
                Debug.WriteLine("Text to speak is empty");
                return;
            }

            if (IsPlaying)
            {
                StopTTS();
            }
            else
            {
                var mainWindow = Window.GetWindow(_editor) as Universa.Desktop.Views.MainWindow;
                if (mainWindow?.TTSClient != null)
                {
                    _ = mainWindow.TTSClient.SpeakAsync(textToSpeak);
                }
            }
        }

        public void StopTTS()
        {
            if (_ttsClient != null)
            {
                _ttsClient.Stop();
                _textHighlighter?.ClearHighlights();
                IsPlaying = false;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTabState(null);
                }));
            }
        }

        public string GetTextToSpeak(string selectedText, string fullText)
        {
            return !string.IsNullOrEmpty(selectedText) ? selectedText : fullText;
        }

        public void OnPlaybackStarted()
        {
            if (_disposed) return;
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPlaying = true;
                UpdateTabState(null);
            });
        }

        public void OnPlaybackCompleted()
        {
            if (_disposed) return;
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPlaying = false;
                UpdateTabState(null);
            });
        }

        public void OnHighlightText(string text)
        {
            if (_disposed || _textHighlighter == null || _editor == null) return;
            
            Debug.WriteLine($"OnHighlightText event received for text: {text}");
            
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Always clear existing highlights first
                        _textHighlighter.ClearHighlights();

                        if (!string.IsNullOrEmpty(text))
                        {
                            Debug.WriteLine($"Highlighting text: {text}");
                            Debug.WriteLine($"Editor text length: {_editor.Text.Length}");
                            
                            // Get the text being played from TTSClient
                            if (_ttsClient != null && !string.IsNullOrEmpty(_ttsClient.CurrentText))
                            {
                                Debug.WriteLine($"Attempting to highlight TTS text: '{_ttsClient.CurrentText}'");
                                
                                // Wait a brief moment to ensure previous highlight is cleared
                                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _textHighlighter.HighlightText(_ttsClient.CurrentText, Colors.Yellow);
                                }), DispatcherPriority.Background);
                            }
                            else
                            {
                                Debug.WriteLine("No current TTS text available");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Clearing text highlights");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in UI thread highlighting: {ex.Message}");
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error dispatching highlight operation: {ex.Message}");
            }
        }

        public void UpdateTabState(Action<bool> updateStateCallback)
        {
            // If a callback is provided, use it to update the tab state
            updateStateCallback?.Invoke(IsPlaying);
        }

        private void SubscribeTTSEvents()
        {
            if (_ttsClient == null) return;
            
            Debug.WriteLine("Setting up TTS event handlers in service");
            _ttsClient.OnHighlightText += TTSClient_OnHighlightText;
            _ttsClient.OnPlaybackStarted += TTSClient_OnPlaybackStarted;
            _ttsClient.OnPlaybackCompleted += TTSClient_OnPlaybackCompleted;
            Debug.WriteLine("TTS event handlers set up successfully in service");
        }

        private void UnsubscribeTTSEvents()
        {
            if (_ttsClient == null) return;
            
            _ttsClient.OnHighlightText -= TTSClient_OnHighlightText;
            _ttsClient.OnPlaybackStarted -= TTSClient_OnPlaybackStarted;
            _ttsClient.OnPlaybackCompleted -= TTSClient_OnPlaybackCompleted;
        }

        private void TTSClient_OnHighlightText(object sender, string text)
        {
            OnHighlightText(text);
        }

        private void TTSClient_OnPlaybackStarted(object sender, EventArgs e)
        {
            OnPlaybackStarted();
        }

        private void TTSClient_OnPlaybackCompleted(object sender, EventArgs e)
        {
            OnPlaybackCompleted();
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            UnsubscribeTTSEvents();
            _ttsClient = null;
            _textHighlighter = null;
            _editor = null;
            PlayingStateChanged = null;
        }
    }
} 