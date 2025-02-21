
namespace SRTimestampFileGenerator
{
    /// <summary>
    /// Main entry point
    /// </summary>
    public class Program
    {
        static void Main(string[] args)
        {
            var logger = new SRLogHandler();
            var downloadManager = new DownloadManager(logger);
        }
    }
}