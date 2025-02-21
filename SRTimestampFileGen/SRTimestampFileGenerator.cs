using SRTimestampLib;

namespace SRTimestampFileGenerator
{
    /// <summary>
    /// Main entry point
    /// </summary>
    public class SRTimestampFileGenerator
    {
        static async Task Main(string[] args)
        {
            var logger = new SRLogHandler();
            var customFileManager = new CustomFileManager(logger);

            await customFileManager.Initialize();
            customFileManager.RefreshLocalMapTimestampMapping();
        }
    }
}