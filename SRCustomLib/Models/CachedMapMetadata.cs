using MemoryPack;

namespace SRCustomLib.Models
{
    [MemoryPackable]
    public partial class CachedMapMetadata
    {
        public Dictionary<string, MapMetadata> MetadataByFileName { get; set; } = new();
    }
}