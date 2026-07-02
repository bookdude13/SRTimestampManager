namespace SRCustomLib
{
    public record DownloadResult(bool Success, int NewMapsFound)
    {
        public static DownloadResult failure = new(false, 0);
        public bool Success { get; } = Success;
        public int NewMapsFound { get; } = NewMapsFound;
    }
}