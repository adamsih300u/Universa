using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public interface IMusicDataService
    {
        // Data retrieval methods
        Task<List<Artist>> GetArtistsAsync();
        Task<List<Album>> GetAlbumsAsync();
        Task<List<Playlist>> GetPlaylistsAsync();
        Task<List<Track>> GetTracksForAlbumAsync(string albumId);
        Task<List<Track>> GetTracksForPlaylistAsync(string playlistId);
        
        // Search methods
        Task<Album> FindAlbumByNameAsync(string albumName, string artistName);
        Task<Album> FindAlbumByIdAsync(string albumId);
        
        // Cache methods
        Task SaveToCacheAsync();
        Task<bool> LoadFromCacheAsync();
        Task ClearCacheAsync();
        
        // Event for data changes
        event EventHandler DataChanged;
    }
    
    // Define model classes if they don't exist
    
    public class Artist
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public List<Album> Albums { get; set; } = new List<Album>();
    }
} 