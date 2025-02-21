using SRTimestampLib;

namespace SRTimestampFixer
{
    internal class SRTimestampFixer
    {
        static async Task Main(string[] args)
        {
            var logger = new SRLogHandler();
            var customFileManager = new CustomFileManager(logger);
            //var downloadManager = new DownloadManager(logger, customFileManager);

            // Initialize
            await customFileManager.Initialize();
            var localMappings = customFileManager.GetLocalTimestampMappings();

            logger.DebugLog($"{localMappings.MapTimestamps.Count} mappings found");

            // Apply to all local files
            customFileManager.ApplyLocalMappings(localMappings);
        }
    }
}
