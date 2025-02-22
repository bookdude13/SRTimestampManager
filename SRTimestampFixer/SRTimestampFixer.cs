using System.IO;
using System.Threading.Tasks;
using SRTimestampLib;

namespace SRTimestampFixer
{
#if !UNITY_2021_3_OR_NEWER // Ignore in Unity
    internal class SRTimestampFixer
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
            var localMappings = customFileManager.GetLocalTimestampMappings();

            logger.DebugLog($"{localMappings.MapTimestamps.Count} mappings found");

            // Apply saved timestamp values to all local files
            customFileManager.ApplyLocalMappings(localMappings);
        }
    }
#endif
}
