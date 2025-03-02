using System;

namespace Universa.Desktop.Models
{
    public enum AudiobookItemType
    {
        Audiobook,
        Podcast,
        PodcastEpisode
    }

    public class AudiobookItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Narrator { get; set; }
        public double Duration { get; set; }
        public string CoverPath { get; set; }
        public double Progress { get; set; }
        public AudiobookItemType Type { get; set; }
        public string Series { get; set; }
        public string SeriesSequence { get; set; }
        public DateTime PublishedAt { get; set; }

        public TimeSpan DurationTimeSpan => TimeSpan.FromSeconds(Duration);
        public TimeSpan ProgressTimeSpan => TimeSpan.FromSeconds(Duration * (Progress / 100));
        public string DisplayDuration => DurationTimeSpan.ToString(@"hh\:mm\:ss");
        public string DisplayProgress => ProgressTimeSpan.ToString(@"hh\:mm\:ss");
        public string DisplayTitle
        {
            get
            {
                var title = Title;
                if (!string.IsNullOrEmpty(Series) && !string.IsNullOrEmpty(SeriesSequence))
                {
                    title = $"{Series} #{SeriesSequence} - {Title}";
                }
                return $"{title} by {Author}";
            }
        }
    }
} 