namespace SRCustomLib
{
    public record struct DownloadResult(bool Success, int NewMapsFound)
    {
        public static DownloadResult failure = new(false, 0);
    }
}