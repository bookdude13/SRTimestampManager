namespace SRTimestampLib
{
#if !UNITY_2021_3_OR_NEWER // Ignore in Unity
    /// <summary>
    /// Abstracts logging. Matches with SRQuestDownloader
    /// </summary>
    public class SRLogHandler
    {
        public void DebugLog(string message) => Debug.Log(message);
        public void ErrorLog(string message) => Debug.LogError(message);
    }
#endif
}
