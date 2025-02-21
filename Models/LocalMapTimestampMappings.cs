using System;
using System.Collections.Generic;

namespace Unity.Template.VR.Models
{
    /// <summary>
    /// All known map -> timestamp mappings that can be applied offline, without the Z site
    /// </summary>
    [Serializable]
    public class LocalMapTimestampMappings
    {
        public List<MapTimestampPair> MapTimestampPairs { get; set; }
    }
}