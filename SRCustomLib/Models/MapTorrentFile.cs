using MonoTorrent;
using SRTimestampLib.Models;

namespace SRCustomLib.Models
{
    public class MapTorrentFile
    {
        public string FileName { get; set; }

        public MapTorrentFile(ITorrentFile file, MapMetadata metadata)
        {
            FileName = file.Path.Trim();
        }
    }
}