using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Universa.Desktop.Services
{
    public class JellyfinStreamService
    {
        private readonly HttpClient _httpClient;
        private readonly JellyfinAuthService _authService;

        public JellyfinStreamService(HttpClient httpClient, JellyfinAuthService authService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        public string GetStreamUrl(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                System.Diagnostics.Debug.WriteLine("JellyfinStreamService.GetStreamUrl: itemId is null or empty");
                return null;
            }

            var baseUrl = _authService.ServerUrl;
            var token = _authService.AccessToken;

            System.Diagnostics.Debug.WriteLine($"JellyfinStreamService.GetStreamUrl: itemId={itemId}, baseUrl={baseUrl ?? "NULL"}, token={(!string.IsNullOrEmpty(token) ? "PRESENT" : "NULL")}");

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("JellyfinStreamService.GetStreamUrl: Missing baseUrl or token, returning null");
                return null;
            }

            // Build streaming URL with direct play parameters
            var streamUrl = $"{baseUrl}/Videos/{itemId}/stream" +
                           "?Static=true" +
                           "&MediaSourceId=" + itemId +
                           "&DeviceId=UniversaApp" +
                           "&api_key=" + token;

            System.Diagnostics.Debug.WriteLine($"JellyfinStreamService.GetStreamUrl: Generated streamUrl: {streamUrl}");
            return streamUrl;
        }

        public string GetTranscodeUrl(string itemId, int? maxBitrate = null, string audioCodec = null, string videoCodec = null)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return null;
            }

            var baseUrl = _authService.ServerUrl;
            var token = _authService.AccessToken;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
            {
                return null;
            }

            var url = $"{baseUrl}/Videos/{itemId}/master.m3u8" +
                     "?MediaSourceId=" + itemId +
                     "&DeviceId=UniversaApp" +
                     "&api_key=" + token;

            if (maxBitrate.HasValue)
            {
                url += "&maxStreamingBitrate=" + maxBitrate.Value;
            }

            if (!string.IsNullOrEmpty(audioCodec))
            {
                url += "&AudioCodec=" + audioCodec;
            }

            if (!string.IsNullOrEmpty(videoCodec))
            {
                url += "&VideoCodec=" + videoCodec;
            }

            return url;
        }

        public async Task<bool> MarkAsWatchedAsync(string itemId, bool watched)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    return false;
                }

                var endpoint = watched ? "Played" : "Unplayed";
                var url = $"{_authService.ServerUrl}/Users/{_authService.UserId}/PlayedItems/{itemId}";

                HttpResponseMessage response;
                if (watched)
                {
                    response = await _httpClient.PostAsync(url, null);
                }
                else
                {
                    response = await _httpClient.DeleteAsync(url);
                }

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Successfully marked item {itemId} as {(watched ? "watched" : "unwatched")}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Failed to mark item {itemId} as {(watched ? "watched" : "unwatched")}: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Error marking item as watched - {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReportPlaybackStartAsync(string itemId, long positionTicks = 0)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    return false;
                }

                var playbackInfo = new
                {
                    ItemId = itemId,
                    SessionId = "UniversaApp",
                    PositionTicks = positionTicks,
                    IsPaused = false,
                    IsMuted = false,
                    AudioStreamIndex = 1,
                    SubtitleStreamIndex = (int?)null,
                    VolumeLevel = 100,
                    CanSeek = true,
                    PlayMethod = "DirectPlay"
                };

                var json = JsonSerializer.Serialize(playbackInfo);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_authService.ServerUrl}/Sessions/Playing", content);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Successfully reported playback start for item {itemId}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Failed to report playback start: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Error reporting playback start - {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReportPlaybackProgressAsync(string itemId, long positionTicks, bool isPaused = false)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    return false;
                }

                var progressInfo = new
                {
                    ItemId = itemId,
                    SessionId = "UniversaApp",
                    PositionTicks = positionTicks,
                    IsPaused = isPaused,
                    IsMuted = false,
                    AudioStreamIndex = 1,
                    SubtitleStreamIndex = (int?)null,
                    VolumeLevel = 100,
                    CanSeek = true,
                    PlayMethod = "DirectPlay"
                };

                var json = JsonSerializer.Serialize(progressInfo);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_authService.ServerUrl}/Sessions/Playing/Progress", content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Failed to report playback progress: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Error reporting playback progress - {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReportPlaybackStopAsync(string itemId, long positionTicks)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    return false;
                }

                var stopInfo = new
                {
                    ItemId = itemId,
                    SessionId = "UniversaApp",
                    PositionTicks = positionTicks,
                    PlayMethod = "DirectPlay"
                };

                var json = JsonSerializer.Serialize(stopInfo);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_authService.ServerUrl}/Sessions/Playing/Stopped", content);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Successfully reported playback stop for item {itemId}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Failed to report playback stop: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinStreamService: Error reporting playback stop - {ex.Message}");
                return false;
            }
        }
    }
} 