using System.Threading.Tasks;
using SRTimestampLib;

namespace SRTimestampFileGenerator
{
#if !UNITY_2021_3_OR_NEWER // Ignore in Unity
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
#endif
}