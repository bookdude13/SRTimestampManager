namespace SRTimestampLib
{
    /// <summary>
    /// Abstracts logging. Matches with SRQuestDownloader
    /// </summary>
    public class SRLogHandler
    {
        public void DebugLog(string message) => Debug.Log(message);
        public void ErrorLog(string message) => Debug.LogError(message);
    }
}
