using SRTimestampLib;

namespace SRTimestampFixerFakeTimes
{
    internal class SRTimestampFixerFakeTimes
    {
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
            
            // Alternative, apply timestamps based on ids
            logger.DebugLog("Using map IDs as fake timestamps for ordering");
            customFileManager.ApplyTimestampsFromIds();
        }
    }
}