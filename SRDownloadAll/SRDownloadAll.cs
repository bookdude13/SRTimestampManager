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
        
        // If we have an override for the synth directory, use that
        if (args.Length > 0)
        {
            var synthDir = args[0];
            Debug.Log($"Override synth dir is '{synthDir}'");
            if (!Directory.Exists(synthDir))
            {
                Debug.LogError($"Override synth dir '{synthDir}' doesn't exist!");
                return;
            }

            FileUtils.OverrideSynthCustomContentDir = Path.GetFullPath(synthDir);
        }
        
        var logger = new SRLogHandler();
        var customFileManager = new CustomFileManager(logger);

        var mapRepo = new MapRepo(logger, useZ: true, useSyn: true, useTorrent: true, customFileManager);

        // Init knowledge of local map files
        await mapRepo.Initialize();

        // Download all missing songs (for now)
        // var lastStartTimeSec = 1738453212L;
        var lastStartTimeSec = customFileManager.db.LastFetchTimestampSec;
        logger.DebugLog($"Last fetched time is {lastStartTimeSec}");
        
        // Downloading all; use ridiculously early time as our start
        bool success = await mapRepo.TryDownloadWithFallbacks(DateTime.UnixEpoch, null, CancellationToken.None);
        if (success)
        {
            customFileManager.db.SetLastDownloadedTime(runStartTime);
            await customFileManager.db.Save();
        }
    }
}
#endif