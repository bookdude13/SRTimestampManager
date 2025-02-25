using SRCustomLib;
using SRTimestampLib;

#if !UNITY_2021_3_OR_NEWER // Ignore in Unity
namespace SRDownloadAll;

/// <summary>
/// Downloads all custom songs for SR
/// </summary>
class SRDownloadAll
{
    static async Task Main(string[] args)
    {
        var runStartTime = DateTime.UtcNow;
        
        var logger = new SRLogHandler();
        var customFileManager = new CustomFileManager(logger);

        // Init knowledge of local map files
        await customFileManager.Initialize();

        var repo = new CustomMapRepoTorrent(logger);

        // Get current state of torrent
        await repo.Initialize();

        // Start with a clean download dir, so everything can be moved over via the torrent itself
        FileUtils.EmptyDirectory(FileUtils.TorrentDownloadDirectory);

        // Download all missing songs (for now)
        // var lastStartTimeSec = 1738453212L;
        var lastStartTimeSec = customFileManager.db.LastFetchTimestampSec;
        logger.DebugLog($"Last fetched time is {lastStartTimeSec}");
        
        // Downloading all; use ridiculously early time as our start
        var downloadedMaps = await repo.DownloadMaps(includedDifficulties: null, startTimestampSec: 0);

        // Only bother with imports and db updates if there were actually any new songs updated
        if (downloadedMaps.Count > 0)
        {
            await customFileManager.AddLocalMaps(downloadedMaps.Select(mapMetadata => mapMetadata.FilePath).ToList());
            var numProcessed = 0;
            foreach (var map in downloadedMaps)
            {
                await customFileManager.AddLocalMap(map.FilePath, null);

                numProcessed++;
                if (numProcessed % 10 == 0)
                {
                    await Task.Yield();
                }
            }
            await customFileManager.db.Save();
        
            // Might as well fix the timestamps while we're here :)
            await customFileManager.ApplyLocalMappings(customFileManager.GetLocalTimestampMappings());
        
            // Update the actual SR database as well, for faster game import (and ensured accuracy)
            await customFileManager.UpdateSynthDBTimestamps();            
        }
        
        customFileManager.db.SetLastDownloadedTime(runStartTime);
        await customFileManager.db.Save();
    }
}
#endif