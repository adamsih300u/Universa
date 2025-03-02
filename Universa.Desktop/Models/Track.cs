using System;

namespace Universa.Desktop.Models
{
    public class Track
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string StreamUrl { get; set; }
        public string CoverArtUrl { get; set; }
        public string Series { get; set; }
        public string Season { get; set; }
        public int TrackNumber { get; set; }
        public bool IsVideo { get; set; }

        public Track Clone()
        {
            return new Track
            {
                Id = this.Id,
                Title = this.Title,
                Artist = this.Artist,
                Album = this.Album,
                Duration = this.Duration,
                StreamUrl = this.StreamUrl,
                CoverArtUrl = this.CoverArtUrl,
                Series = this.Series,
                Season = this.Season,
                TrackNumber = this.TrackNumber,
                IsVideo = this.IsVideo
            };
        }
    }
} 