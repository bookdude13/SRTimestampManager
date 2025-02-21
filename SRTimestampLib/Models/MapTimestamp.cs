using System;

namespace SRTimestampLib.Models
{
    /// <summary>
    /// Correlates a map (hash) and its timstamp, for fixing timestamps
    /// </summary>
    [Serializable]
    public struct MapTimestamp
    {
        public string hash;
        public long modified_time;
        public string modified_time_formatted;
    }
}