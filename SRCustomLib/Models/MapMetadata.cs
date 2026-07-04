using System;
using System.Collections.Generic;
using System.Linq;
using SRTimestampLib.Models;
#if !UNITY_6000_2_OR_NEWER
using MemoryPack;
#endif

// Avoid annoying warnings in Unity
#nullable enable

namespace SRCustomLib.Models
{
    /// <summary>
    /// Information about a custom map, used for search/filter
    /// </summary>
#if UNITY_6000_2_OR_NEWER
    [Serializable]
#else
    [MemoryPackable]
#endif
    public partial class MapMetadata
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

        public static MapMetadata FromMapItem(MapItem mapItem)
        {
            DateTime publishedAtTime = mapItem.GetPublishedAtUtc() ?? DateTime.UnixEpoch;
            return new MapMetadata
            {
                FileName = mapItem.filename,
                Hash = mapItem.hash,
                MapName = mapItem.title,
                Mapper = mapItem.mapper,
                Duration = mapItem.duration,
                SongArtist = mapItem.artist,
                PublishedAtTimestampSec = (long)(publishedAtTime - DateTime.UnixEpoch).TotalSeconds,
                SupportedDifficulties = (mapItem.difficulties ?? Array.Empty<string>()).Where(diff => !string.IsNullOrEmpty(diff)).ToList(),
            };
        }
    }
}