using Newtonsoft.Json;

namespace SRTimestampLib.Models
{
    /// <summary>
    /// All known map -> timestamp mappings that can be applied offline, without the Z site
    /// </summary>
    [Serializable]
    public class LocalMapTimestampMappings
    {
        public List<MapItem> MapTimestamps = new();

        [JsonIgnore]
        private Dictionary<string, DateTime> _hashToDateModifiedUtc = new();

        public void Add(MapItem mapTimestamp)
        {
            if (string.IsNullOrEmpty(mapTimestamp.hash))
                return;
            
            MapTimestamps.Add(mapTimestamp);

            // Convert to DateTime for easier comparison/use in other APIs
            // var dateModifiedUtc = DateTime.UnixEpoch + TimeSpan.FromSeconds(mapTimestamp.modified_time);
            var dateModifiedUtc = mapTimestamp.GetPublishedAtUtc();
            if (dateModifiedUtc == null)
            {
                return;
            }
            //Debug.Log($"Time {mapTimestamp.modified_time} => {dateModifiedUtc.ToString()}");

            if (_hashToDateModifiedUtc.ContainsKey(mapTimestamp.hash))
            {
                Debug.LogError($"Duplicate entry in file for hash {mapTimestamp.hash}. Times are {dateModifiedUtc.Value} and {_hashToDateModifiedUtc[mapTimestamp.hash]}");
                return;
            }
            _hashToDateModifiedUtc.Add(mapTimestamp.hash, dateModifiedUtc.Value);
        }

        public bool TryGetDateModified(string hash, out DateTime dateModified)
        {
            return _hashToDateModifiedUtc.TryGetValue(hash, out dateModified);
        }
    }
}