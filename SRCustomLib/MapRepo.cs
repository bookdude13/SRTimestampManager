using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SRTimestampLib;

namespace SRCustomLib
{
    /// <summary>
    /// Retrieve SynthRiders maps from various sources
    /// </summary>
    public class MapRepo
    {
        private readonly SRLogHandler _logger;
        private readonly CustomFileManager _customFileManager;
        private readonly DownloadManagerZ _repoZ;
        private readonly DownloadManagerSyn _repoSyn;
        private readonly CustomMapRepoTorrent _repoTorrent;

        private bool _useZ;
        private bool _useSyn;
        private bool _useTorrent;
        
        public MapRepo(SRLogHandler logger, bool useZ = true, bool useSyn = true, bool useTorrent = true, CustomFileManager? customFileManager = null)
        {
            _logger = logger;

            _customFileManager = customFileManager ?? new CustomFileManager(logger);
            
            _useZ = useZ;
            _useSyn = useSyn;
            _useTorrent = useTorrent;
            
            _repoZ = new DownloadManagerZ(logger, _customFileManager);
            _repoSyn = new DownloadManagerSyn(logger, _customFileManager);
            _repoTorrent = new CustomMapRepoTorrent(logger, _customFileManager);
        }

        public async Task Initialize()
        {
            await _customFileManager.Initialize();

            // Start with a clean download dir, so everything can be moved over via the torrent itself
            FileUtils.EmptyDirectory(FileUtils.TorrentDownloadDirectory);
        }
        
        /// <summary>
        /// Tries to download songs since the given cutoffTime, with any of the given difficulties, from various sources (fallbacks as necessary)
        /// </summary>
        /// <param name="cutoffTimeUtc"></param>
        /// <param name="difficultySelections"></param>
        /// <returns></returns>
        public async Task<bool> TryDownloadWithFallbacks(DateTime cutoffTimeUtc, List<string>? difficultySelections, CancellationToken cancellationToken)
        {
            var success = false;

            // First, try Z download
            if (_useZ)
            {
                _logger.DebugLog("Attempting to download from Z...");
                success = await _repoZ.DownloadSongsSinceTime(cutoffTimeUtc, difficultySelections, cancellationToken);
            }
        
            if (!success && _useSyn)
            {
                // Fallback on synplicity
                _logger.DebugLog("Attempting to download from Synplicity...");
                return await _repoSyn.DownloadSongsSinceTime(cutoffTimeUtc, difficultySelections, cancellationToken);
            }

            if (!success && _useTorrent)
            {
                // Fallback on torrent

                // Ensure the torrent repo is initialized. Done here so it doesn't have to happen if we have a working site
                if (!_repoTorrent.IsInitialized)
                {
                    _logger.DebugLog("Setting up torrent map source...");
                    await _repoTorrent.Initialize();
                }

                _logger.DebugLog("Attempting to download from torrent...");
                // TODO get difficulty info to filter from torrent as well
                var diffSet = difficultySelections == null ? new() : new HashSet<string>(difficultySelections);
                var downloadedMaps = await _repoTorrent.DownloadMaps(null, cutoffTimeUtc);
                success = downloadedMaps != null;
            }

            return success;
        }
    }
}