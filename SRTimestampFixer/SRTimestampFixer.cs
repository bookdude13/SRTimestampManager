using SRTimestampLib;

namespace SRTimestampFixer
{
    internal class SRTimestampFixer
    {
        private const bool USE_FAKE_TIMESTAMP_IDS = true;
        
        static async Task Main(string[] args)
        {
            var logger = new SRLogHandler();
            
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
            
            var customFileManager = new CustomFileManager(logger);

            // Initialize
            await customFileManager.Initialize();

            if (USE_FAKE_TIMESTAMP_IDS)
            {
                // Alternative, apply timestamps based on ids
                logger.DebugLog("Using map IDs as fake timestamps for ordering");
                customFileManager.ApplyTimestampsFromIds();
            }
            else
            {
                var localMappings = customFileManager.GetLocalTimestampMappings();

                logger.DebugLog($"{localMappings.MapTimestamps.Count} mappings found");

                // Apply saved timestamp values to all local files
                customFileManager.ApplyLocalMappings(localMappings);
            }
        }
    }
}
