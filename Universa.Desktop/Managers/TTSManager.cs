using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Linq;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;
using Universa.Desktop.Windows;
using TTSClient = Universa.Desktop.Core.TTS.TTSClient;
using Universa.Desktop.Core.TTS;
using Universa.Desktop.Views;

namespace Universa.Desktop.Managers
{
    public class TTSManager : IDisposable
    {
        private readonly IMediaWindow _mainWindow;
        private readonly IConfigurationService _configService;
        private TTSClient _ttsClient;
        private CancellationTokenSource _ttsCancellation;
        private MediaPlayer _ttsMediaPlayer;
        private string _tempAudioFile;
        private bool _isTTSPlaying;
        private object _currentTTSTab;
        private bool _disposed;

        public event EventHandler<string> OnSpeechStarted;
        public event EventHandler<string> OnSpeechCompleted;
        public event EventHandler<string> OnError;

        public TTSClient TTSClient => _ttsClient;
        public bool IsTTSPlaying => _isTTSPlaying;
        public object CurrentTTSTab => _currentTTSTab;
        public bool IsInitialized => _ttsClient != null;
        public bool IsConnected => _ttsClient?.IsConnected ?? false;
        public string CurrentVoice => _ttsClient?.CurrentVoice;

        public TTSManager(IMediaWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _ttsMediaPlayer = new MediaPlayer();
            _configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            InitializeTTS();
        }

        private void InitializeTTS()
        {
            if (_configService?.Provider == null) return;

            if (_configService.Provider.EnableTTS &&
                !string.IsNullOrEmpty(_configService.Provider.TTSApiUrl) &&
                !string.IsNullOrEmpty(_configService.Provider.TTSVoice))
            {
                _ttsClient = new TTSClient(
                    _configService.Provider.TTSApiUrl,
                    _configService.Provider.TTSVoice
                );
                _ttsClient.OnConnected += (s, msg) => Debug.WriteLine($"TTS: {msg}");
                _ttsClient.OnDisconnected += (s, msg) => Debug.WriteLine($"TTS: {msg}");
                _ttsClient.OnError += (s, msg) => Debug.WriteLine($"TTS: {msg}");
                _ttsClient.OnVoiceSet += (s, msg) => Debug.WriteLine($"TTS: {msg}");
                _ttsClient.OnAudioReceived += async (s, data) => 
                {
                    Debug.WriteLine($"Received audio for text: {data.text}");
                    await PlayAudioAsync(data.audio);
                };
                _ttsClient.OnVoicesAvailable += (s, voices) =>
                {
                    Debug.WriteLine($"Received {voices.Length} voices from TTS server");
                    _configService.Provider.TTSAvailableVoices = voices.ToList();
                    _configService.Provider.Save();
                };

                // Initial connection attempt - fail silently
                _ = ConnectTTSClientAsync();
            }
        }

        private async Task ConnectTTSClientAsync()
        {
            try
            {
                await _ttsClient.ConnectAsync();
                if (!string.IsNullOrEmpty(_configService.Provider.TTSVoice))
                {
                    await _ttsClient.SetVoiceAsync(_configService.Provider.TTSVoice);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initial TTS connection failed: {ex.Message}");
                // Fail silently on startup
            }
        }

        public async Task StartTTS(string textToSpeak, object sourceTab)
        {
            Debug.WriteLine("StartTTS called");
            try
            {
                if (_ttsClient == null)
                {
                    MessageBox.Show("Please configure TTS settings first.", "TTS Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(textToSpeak))
                {
                    MessageBox.Show("No text to speak.", "Empty Text", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // If we're already playing TTS for this tab, stop it
                if (_isTTSPlaying && sourceTab == _currentTTSTab)
                {
                    StopTTS();
                    return;
                }

                // If we're playing TTS for a different tab, stop that first
                if (_isTTSPlaying)
                {
                    StopTTS();
                }

                // Try to reconnect if not connected
                if (!_ttsClient.IsConnected)
                {
                    Debug.WriteLine("TTS client not connected, attempting to reconnect...");
                    try
                    {
                        await _ttsClient.ConnectAsync();
                        if (!string.IsNullOrEmpty(_configService.Provider.TTSVoice))
                        {
                            await _ttsClient.SetVoiceAsync(_configService.Provider.TTSVoice);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"TTS reconnection failed: {ex.Message}");
                        MessageBox.Show("Unable to connect to TTS server. Please check your settings and try again.", 
                            "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                _currentTTSTab = sourceTab;
                _isTTSPlaying = true;
                _ttsCancellation = new CancellationTokenSource();
                await _ttsClient.SpeakAsync(textToSpeak);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TTS error: {ex}");
                MessageBox.Show($"Error during TTS: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task PlayAudioAsync(byte[] audioData)
        {
            try
            {
                Debug.WriteLine($"Playing audio data of length: {audioData.Length} bytes");
                
                // Create a temporary file in the system temp directory
                string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
                
                // Write the audio data to the temp file asynchronously
                await Task.Run(() => File.WriteAllBytes(tempFile, audioData));
                
                // Clean up previous temp file if it exists
                if (!string.IsNullOrEmpty(_tempAudioFile) && File.Exists(_tempAudioFile))
                {
                    try
                    {
                        await Task.Run(() => File.Delete(_tempAudioFile));
                    }
                    catch { /* Ignore cleanup errors */ }
                }
                
                _tempAudioFile = tempFile;
                
                // Use TaskCompletionSource to handle MediaOpened event
                var mediaOpenedTcs = new TaskCompletionSource<bool>();
                
                EventHandler mediaOpenedHandler = null;
                mediaOpenedHandler = (s, e) =>
                {
                    _ttsMediaPlayer.MediaOpened -= mediaOpenedHandler;
                    mediaOpenedTcs.SetResult(true);
                };
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _ttsMediaPlayer.MediaOpened += mediaOpenedHandler;
                    
                    // Stop any current playback
                    _ttsMediaPlayer.Stop();
                    
                    // Open and play the media file
                    _ttsMediaPlayer.Open(new Uri(tempFile));
                });
                
                // Wait for media to open (with timeout)
                await Task.WhenAny(mediaOpenedTcs.Task, Task.Delay(2000));
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _ttsMediaPlayer.Play();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Audio playback error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error playing audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void StopTTS()
        {
            _isTTSPlaying = false;
            _currentTTSTab = null;

            // Stop playback
            if (_ttsMediaPlayer != null)
            {
                _ttsMediaPlayer.Stop();
            }
            
            // Clean up temp file immediately
            if (!string.IsNullOrEmpty(_tempAudioFile) && File.Exists(_tempAudioFile))
            {
                try
                {
                    File.Delete(_tempAudioFile);
                    _tempAudioFile = null;
                }
                catch { /* Ignore cleanup errors */ }
            }
            
            // Cancel any pending TTS operations
            if (_ttsCancellation != null)
            {
                _ttsCancellation.Cancel();
                _ttsCancellation.Dispose();
                _ttsCancellation = null;
            }

            // Stop TTS client
            if (_ttsClient != null)
            {
                _ttsClient.Stop();
                // Attempt to reconnect the WebSocket for future use
                _ = ConnectTTSClientAsync();
            }
        }

        public async Task SetVoiceAsync(string voice)
        {
            if (_ttsClient == null)
            {
                throw new InvalidOperationException("TTS not initialized. Call InitializeTTSAsync first.");
            }

            await _ttsClient.SetVoiceAsync(voice);
            _configService.SetValue("TTS:Voice", voice);
            await _configService.SaveAsync();
        }

        public async Task<byte[]> SynthesizeSpeechAsync(string text)
        {
            if (_ttsClient == null)
                throw new InvalidOperationException("TTS client is not initialized");

            return await _ttsClient.SynthesizeSpeechAsync(text);
        }

        public async Task<string[]> GetAvailableVoicesAsync()
        {
            if (_ttsClient == null)
            {
                throw new InvalidOperationException("TTS not initialized. Call InitializeTTSAsync first.");
            }

            return await _ttsClient.GetAvailableVoicesAsync();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    StopTTS();
                    _ttsMediaPlayer = null;
                    _ttsClient?.Dispose();
                }

                _disposed = true;
            }
        }

        public async Task InitializeTTSAsync()
        {
            var apiUrl = _configService.GetValue<string>("TTS:ApiUrl");
            var voice = _configService.GetValue<string>("TTS:Voice") ?? "Default";

            _ttsClient = new TTSClient(apiUrl, voice);
            
            _ttsClient.OnConnected += (s, e) => OnSpeechStarted?.Invoke(this, e);
            _ttsClient.OnDisconnected += (s, e) => 
            {
                _isTTSPlaying = false;
                OnSpeechCompleted?.Invoke(this, e);
            };
            _ttsClient.OnError += (s, e) => OnError?.Invoke(this, e);

            await _ttsClient.ConnectAsync();
        }

        public async Task SpeakAsync(string text, object tab = null)
        {
            if (_ttsClient == null)
            {
                throw new InvalidOperationException("TTS not initialized. Call InitializeTTSAsync first.");
            }

            try
            {
                _isTTSPlaying = true;
                _currentTTSTab = tab;
                OnSpeechStarted?.Invoke(this, text);
                await _ttsClient.SpeakAsync(text);
                OnSpeechCompleted?.Invoke(this, text);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex.Message);
                throw;
            }
            finally
            {
                _isTTSPlaying = false;
                _currentTTSTab = null;
            }
        }

        public void Stop()
        {
            if (_ttsClient != null && _isTTSPlaying)
            {
                _ttsClient.Stop();
                _isTTSPlaying = false;
                _currentTTSTab = null;
            }
        }
    }
} 