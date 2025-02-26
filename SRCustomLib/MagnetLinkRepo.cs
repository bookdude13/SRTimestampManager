using MonoTorrent;
using SRTimestampLib;

// Avoid annoying warnings in Unity
#nullable enable

namespace SRCustomLib
{
    /// <summary>
    /// Handles retrieving the latest magnet link and if necessary invalidating the cached torrent.
    /// </summary>
    public class MagnetLinkRepo
    {
        /// <summary>
        /// Stored on Dropbox, publicly accessible and downloadable.
        /// Note - copy the share link, then change the ending "&dl=0" to "&raw=1" to have it get the raw contents
        /// </summary>
        private const string MAGNET_FILE_LINK = "https://www.dropbox.com/scl/fi/kt38cgixmajyalxo8vxfk/magnet_songs.txt?rlkey=sk8quyuymm82sly13ev8kjqwj&st=f88d61u9&raw=1";

        private SRLogHandler _logger = new();
        private HttpClient _client = new();
        private CancellationTokenSource _cts = new();

        /// <summary>
        /// Where to save the cached magnet file. Used for comparing with new magnet files and avoiding extra work
        /// </summary>
        private string SongMagnetFilePath = Path.Combine(FileUtils.GetPersistentFolder(), "magnet_songs.txt");

        ~MagnetLinkRepo()
        {
            _cts.Cancel();
        }
        
        private void RefreshCts(TimeSpan timeout)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource(timeout);
        }

        /// <summary>
        /// Retrieves the magnet link we should use (latest from online source, fallback to local cached if not found, fallback to null)
        /// </summary>
        /// <returns></returns>
        public async Task<MagnetLink?> TryGetMagnetLinkAsync()
        {
            RefreshCts(TimeSpan.FromSeconds(120));
            var rawRemoteMagnet = await GetFileContentsFromUrl(MAGNET_FILE_LINK, _cts.Token);

            MagnetLink? remoteMagnet = null;
            var hasRemoteMagnet = !string.IsNullOrEmpty(rawRemoteMagnet) && MagnetLink.TryParse(rawRemoteMagnet, out remoteMagnet);
            var hasLocalMagnet = TryGetCurrentMagnetLink(out var localMagnet, out var rawLocalMagnet);

            if (hasRemoteMagnet && hasLocalMagnet)
            {
                var isLocalUpToDate = string.Equals(rawRemoteMagnet, rawLocalMagnet, StringComparison.OrdinalIgnoreCase);
                if (isLocalUpToDate)
                {
                    _logger.DebugLog("Using current magnet link (up to date)");
                    return localMagnet;
                }
                else
                {
                    _logger.DebugLog("Updating magnet link!");
                    // Assume the online link is newer; invalidate the cached .torrent
                    FileUtils.DeleteFile(FileUtils.CachedTorrentFile, _logger);

                    return remoteMagnet;
                }
            }
            else if (hasRemoteMagnet && !hasLocalMagnet)
            {
                // First time! Cache for later comparison
                _logger.DebugLog("Caching magnet link");
                await FileUtils.WriteToFile(rawRemoteMagnet!, SongMagnetFilePath, _logger);
                
                // In case this is updating from the version that cached the torrent but didn't save the magnet file,
                // make sure the torrent file gets updated
                FileUtils.DeleteFile(FileUtils.CachedTorrentFile, _logger);
                
                return remoteMagnet;
            }
            else if (!hasRemoteMagnet && hasLocalMagnet)
            {
                // Failed to retrieve from remote source for some reason
                _logger.ErrorLog("Couldn't get latest link from online source; using local cached link");

                return localMagnet;
            }
            else
            {
                _logger.ErrorLog("No local link and couldn't retrieve from online source :(");
                return null;
            }
        }

        private async Task<string?> GetFileContentsFromUrl(string url, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await _client.GetStreamAsync(url, cancellationToken);
                var streamReader = new StreamReader(stream);
                return await streamReader.ReadToEndAsync(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.ErrorLog($"Failed to get contents from url '{url}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the currently cached magnet file contents, if any.
        /// </summary>
        /// <returns>True if it was found and read successfully, false if not</returns>
        private bool TryGetCurrentMagnetLink(out MagnetLink? currentMagnet, out string? rawLink)
        {
            currentMagnet = default;
            rawLink = null;
            
            if (!File.Exists(SongMagnetFilePath))
                return false;
            
            rawLink = File.ReadAllText(SongMagnetFilePath);
            return MagnetLink.TryParse(rawLink, out currentMagnet);
        }
    }
}