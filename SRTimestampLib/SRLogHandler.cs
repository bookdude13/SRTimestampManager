namespace SRTimestampLib
{
    /// <summary>
    /// Abstracts logging. Matches with SRQuestDownloader
    /// </summary>
    public interface ISRLogHandler
    {
        public void DebugLog(string message);
        public void ErrorLog(string message);
    }
    
    public class SRLogHandler : ISRLogHandler
    {
        public void DebugLog(string message) => Debug.Log(message);
        public void ErrorLog(string message) => Debug.LogError(message);
    }
}
