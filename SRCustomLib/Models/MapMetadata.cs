using System.Collections.Generic;

// Avoid annoying warnings in Unity
#nullable enable

namespace SRCustomLib.Models
{
    /// <summary>
    /// Information about a custom map, used for search/filter
    /// </summary>
    public class MapMetadata
    {
        public string? FileName { get; set; }
        public string? DownloadedPath { get; set; }
        public string? Hash { get; set; }

        public string? MapName { get; set; }
        public string? SongArtist { get; set; }
        public string? Duration { get; set; }
        public string? Mapper { get; set; }
        public long PublishedAtTimestampSec { get; set; }
        public List<string>? SupportedDifficulties { get; set; }
    }
}