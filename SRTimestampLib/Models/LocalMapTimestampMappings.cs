using System;
using System.Collections.Generic;

namespace SRTimestampLib.Models
{
    /// <summary>
    /// All known map -> timestamp mappings that can be applied offline, without the Z site
    /// </summary>
    [Serializable]
    public class LocalMapTimestampMappings
    {
        public List<MapTimestamp> MapTimestamps = new();

        private Dictionary<string, DateTime> _hashToDateModifiedUtc = new();

        public void Add(MapTimestamp mapTimestamp)
        {
            MapTimestamps.Add(mapTimestamp);

            // Convert to DateTime for easier comparison/use in other APIs
            var dateModifiedUtc = DateTime.UnixEpoch + TimeSpan.FromSeconds(mapTimestamp.modified_time);
            //Debug.Log($"Time {mapTimestamp.modified_time} => {dateModifiedUtc.ToString()}");

            if (_hashToDateModifiedUtc.ContainsKey(mapTimestamp.hash))
            {
                Debug.LogError($"Duplicate entry in file for hash {mapTimestamp.hash}. Times are {dateModifiedUtc} and {_hashToDateModifiedUtc[mapTimestamp.hash]}");
                return;
            }
            _hashToDateModifiedUtc.Add(mapTimestamp.hash, dateModifiedUtc);
        }

        public bool TryGetDateModified(string hash, out DateTime dateModified)
        {
            return _hashToDateModifiedUtc.TryGetValue(hash, out dateModified);
        }
    }
}