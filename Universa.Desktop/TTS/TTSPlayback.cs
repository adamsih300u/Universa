using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Linq;

namespace Universa.Desktop.TTS
{
    public class TTSPlayback : IDisposable
    {
        private SoundPlayer _player;
        private SoundPlayer _nextPlayer;
        private bool _isPlaying;
        private readonly SynchronizationContext _uiContext;
        private string _currentText;
        private MemoryStream _currentStream;
        private MemoryStream _nextStream;

        public string CurrentText
        {
            get => _currentText;
            set => _currentText = value;
        }
        
        public event EventHandler<string> OnHighlightText;
        public event EventHandler OnPlaybackStarted;
        public event EventHandler OnPlaybackCompleted;

        public TTSPlayback()
        {
            _player = new SoundPlayer();
            _nextPlayer = new SoundPlayer();
            _isPlaying = false;
            _uiContext = SynchronizationContext.Current;
        }

        private void HighlightText(string text)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_currentText))
            {
                return;
            }

            try
            {
                // Clean up the text for matching
                var textToMatch = text.Trim();
                
                // Escape special regex characters but allow for flexible whitespace
                var pattern = string.Join(@"\s+",
                    textToMatch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => Regex.Escape(word)));

                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var match = regex.Match(_currentText);

                if (match.Success)
                {
                    Debug.WriteLine($"Found match for text: '{textToMatch}'");
                    Debug.WriteLine($"Match found at index {match.Index} with length {match.Length}");
                    Debug.WriteLine($"Matched text: '{_currentText.Substring(match.Index, match.Length)}'");

                    _uiContext?.Post(_ => 
                    {
                        OnHighlightText?.Invoke(this, $"{match.Index}|{match.Length}");
                    }, null);
                }
                else
                {
                    Debug.WriteLine($"No match found for text: '{textToMatch}'");
                    Debug.WriteLine($"Current text length: {_currentText?.Length ?? 0}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in text highlighting: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task PlayAudioAsync(byte[] audioData, string text, int messageId, int chunkIndex, int totalChunks)
        {
            if (audioData == null || audioData.Length == 0)
            {
                Debug.WriteLine("Received empty audio data");
                return;
            }

            try
            {
                var wavData = EnsureValidWavHeader(audioData);

                // If we're currently playing, load this into the next player
                if (_isPlaying)
                {
                    _nextStream?.Dispose();
                    _nextStream = new MemoryStream(wavData);
                    _nextPlayer.Stream = _nextStream;
                    _nextPlayer.LoadAsync();
                    return;
                }

                // Otherwise, play this audio immediately
                _currentStream?.Dispose();
                _currentStream = new MemoryStream(wavData);
                Debug.WriteLine($"Playing audio with {wavData.Length} bytes");

                // Highlight the text before starting playback
                HighlightText(text);

                var completionSource = new TaskCompletionSource<bool>();

                AsyncCompletedEventHandler loadCompletedHandler = null;
                loadCompletedHandler = async (s, e) =>
                {
                    _player.LoadCompleted -= loadCompletedHandler;
                    
                    if (e.Error != null)
                    {
                        Debug.WriteLine($"Error loading audio: {e.Error.Message}");
                        completionSource.SetException(e.Error);
                        return;
                    }

                    try
                    {
                        _isPlaying = true;
                        OnPlaybackStarted?.Invoke(this, EventArgs.Empty);
                        _player.Play();

                        // Since SoundPlayer doesn't have a completion event,
                        // we'll estimate the duration based on the audio data size
                        // 22050Hz * 16bit * 1 channel = 44100 bytes per second
                        int durationMs = (wavData.Length * 1000) / 44100;
                        await Task.Delay(durationMs);

                        _isPlaying = false;
                        
                        // If we have a next segment ready, swap players and start it
                        if (_nextStream != null)
                        {
                            var tempPlayer = _player;
                            var tempStream = _currentStream;
                            
                            _player = _nextPlayer;
                            _currentStream = _nextStream;
                            
                            _nextPlayer = tempPlayer;
                            _nextStream = null;
                            
                            _player.Play();
                        }
                        else
                        {
                            _currentStream?.Dispose();
                            _currentStream = null;
                            
                            _uiContext?.Post(_ => 
                            {
                                OnPlaybackCompleted?.Invoke(this, EventArgs.Empty);
                            }, null);
                        }
                        
                        completionSource.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error during playback: {ex.Message}");
                        completionSource.SetException(ex);
                    }
                };

                _player.LoadCompleted += loadCompletedHandler;
                _player.Stream = _currentStream;
                _player.LoadAsync();

                await completionSource.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing audio: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public void Stop()
        {
            try
            {
                if (_isPlaying)
                {
                    _player?.Stop();
                    _nextPlayer?.Stop();
                    _isPlaying = false;
                }

                // Dispose streams
                try { _currentStream?.Dispose(); } catch { }
                try { _nextStream?.Dispose(); } catch { }
                _currentStream = null;
                _nextStream = null;

                // Clear highlight
                _uiContext?.Post(_ => 
                {
                    try
                    {
                        OnHighlightText?.Invoke(this, string.Empty);
                        OnPlaybackCompleted?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        // Ignore errors during cleanup
                    }
                }, null);
            }
            catch
            {
                // Ignore any errors during stop
            }
        }

        public void Dispose()
        {
            Stop();

            try
            {
                _player?.Dispose();
                _nextPlayer?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }

            _player = null;
            _nextPlayer = null;
        }

        private byte[] EnsureValidWavHeader(byte[] audioData)
        {
            // Skip if already has WAV header
            if (audioData.Length >= 44 && 
                Encoding.ASCII.GetString(audioData, 0, 4) == "RIFF" &&
                Encoding.ASCII.GetString(audioData, 8, 4) == "WAVE")
            {
                return audioData;
            }
            
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    const int sampleRate = 22050;    // 22.05kHz as specified by server
                    const short channels = 1;        // Mono
                    const short bitsPerSample = 16;  // 16-bit audio
                    int byteRate = sampleRate * channels * (bitsPerSample / 8);
                    short blockAlign = (short)(channels * (bitsPerSample / 8));

                    // RIFF header
                    bw.Write(Encoding.ASCII.GetBytes("RIFF")); // ChunkID
                    bw.Write(audioData.Length + 36); // ChunkSize (file size - 8)
                    bw.Write(Encoding.ASCII.GetBytes("WAVE")); // Format
                    
                    // fmt chunk
                    bw.Write(Encoding.ASCII.GetBytes("fmt ")); // Subchunk1ID
                    bw.Write(16); // Subchunk1Size (16 for PCM)
                    bw.Write((short)1); // AudioFormat (1 for PCM)
                    bw.Write(channels); // NumChannels (1 for mono)
                    bw.Write(sampleRate); // SampleRate (22050 Hz)
                    bw.Write(byteRate); // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
                    bw.Write(blockAlign); // BlockAlign (NumChannels * BitsPerSample/8)
                    bw.Write(bitsPerSample); // BitsPerSample (16 bits)
                    
                    // data chunk
                    bw.Write(Encoding.ASCII.GetBytes("data")); // Subchunk2ID
                    bw.Write(audioData.Length); // Subchunk2Size
                    bw.Write(audioData);
                }
                return ms.ToArray();
            }
        }
    }
} 