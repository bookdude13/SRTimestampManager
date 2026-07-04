using System;
using System.Collections.Generic;
#if !UNITY_6000_2_OR_NEWER
using MemoryPack;
#endif

namespace SRCustomLib.Models
{
    // Error in MemoryPack - https://github.com/Cysharp/MemoryPack/pull/380
#if UNITY_6000_2_OR_NEWER
    [Serializable]
#else
    [MemoryPackable]
#endif
    public partial class CachedMapMetadata
    {
        public Dictionary<string, MapMetadata> MetadataByFileName { get; set; } = new();
    }
}