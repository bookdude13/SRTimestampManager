using SRCustomLib;
using SRTimestampLib;

namespace SRDownloadAll;

/// <summary>
/// Downloads all custom songs for SR
/// </summary>
class SRDownloadAll
{
    static async Task Main(string[] args)
    {
        // var logger = new SRLogHandler();
        // var customFileManager = new CustomFileManager(logger);

        // Init knowledge of local map files
        // await customFileManager.Initialize();

        var repo = new CustomMapRepoTorrent();

        // Get current state of torrent
        await repo.Initialize();

        // Download all missing songs (for now)
        var downloadedMaps = await repo.DownloadMaps(includedDifficulties: null, startTimestampSec: 1738453212L);
    }
}