using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public interface ISubsonicService
    {
        Task<Album> GetAlbumAsync(string albumId);
        Task<TrackInfo> GetTrackInfoAsync(string trackId);
        Task RefreshConfiguration();
        Task<List<MusicItem>> GetAllTracks();
        Task<List<MusicItem>> GetPlaylists();
        Task<List<MusicItem>> GetPlaylistTracks(string playlistId);
        Task AddToPlaylistAsync(string playlistId, string songId);
        Task<bool> AddTracksToPlaylistAsync(string playlistId, List<string> trackIds);
        Task<bool> RemoveTrackFromPlaylistAsync(string playlistId, string trackId, int trackIndex);
        Task CreatePlaylistAsync(string name);
        Task DeletePlaylistAsync(string playlistId);
        Task<List<MusicItem>> GetArtists();
        Task<List<MusicItem>> GetAllAlbums();
        Task<List<MusicItem>> GetTracks(string albumId);
    }

    public class TrackInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public TimeSpan Duration { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string StreamUrl { get; set; }
        public string CoverArtUrl { get; set; }
    }
} 