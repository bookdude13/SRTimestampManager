namespace SRTimestampLib
{
#if !UNITY_2021_3_OR_NEWER // Ignore in Unity
    /// <summary>
    /// Allows for easier mapping between Unity code and this utility
    /// </summary>
    public class Debug
    {
        public static void Log(string message) => Console.WriteLine(message);
        public static void LogError(string message) => Console.Error.WriteLine(message);
    }
#endif
}
