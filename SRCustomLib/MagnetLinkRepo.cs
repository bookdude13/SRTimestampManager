using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using MonoTorrent;
using Newtonsoft.Json;
using SRCustomLib.Models;
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
        /// API to retrieve the latest mirror link
        /// </summary>
        private const string GET_MAGNET_API_URL = "https://synthriderz.com/api/beatmaps/torrent";

        private readonly SRLogHandler _logger;
        private readonly HttpClient _client = new();

        /// <summary>
        /// Where to save the cached magnet file. Used for comparing with new magnet files and avoiding extra work
        /// </summary>
        private string SongMagnetFilePath = Path.Combine(FileUtils.GetPersistentFolder(), "magnet_songs.txt");

        public MagnetLinkRepo(SRLogHandler logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Retrieves the magnet link we should use (latest from online source, fallback to local cached if not found, fallback to null)
        /// </summary>
        /// <returns></returns>
        public async Task<MagnetLink?> TryGetMagnetLinkAsync()
        {
            MagnetLinkInfo? remoteMagnetInfo = await GetMagnetLinkFromUrl(GET_MAGNET_API_URL, TimeSpan.FromSeconds(120));
            string rawRemoteMagnet = remoteMagnetInfo?.magnet_uri ?? "";

            MagnetLink? remoteMagnet = null;
            var hasRemoteMagnet = !string.IsNullOrEmpty(rawRemoteMagnet) && MagnetLink.TryParse(rawRemoteMagnet, out remoteMagnet);
            var hasLocalMagnet = TryGetCurrentMagnetLink(out var localMagnet, out var rawLocalMagnet);

            if (hasRemoteMagnet)
            {
                _logger.DebugLog($"Found remote magnet link, last updated {remoteMagnetInfo!.updated_at}");
            }

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
                    
                    await FileUtils.WriteToFile(rawRemoteMagnet!, SongMagnetFilePath, _logger);

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

        private async Task<MagnetLinkInfo?> GetMagnetLinkFromUrl(string url, TimeSpan timeout)
        {
            try
            {
                _client.Timeout = timeout;
                string? rawContents = await _client.GetStringAsync(url);

                return string.IsNullOrEmpty(rawContents) ? null : JsonConvert.DeserializeObject<MagnetLinkInfo>(rawContents);
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