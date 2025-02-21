using System;

namespace Unity.Template.VR.Models
{
    /// <summary>
    /// Correlates a map (hash) and its timstamp, for fixing timestamps
    /// </summary>
    [Serializable]
    public struct MapTimestampPair
    {
        public string MapHash;
        public DateTime DateModifiedUtc;
    }
}