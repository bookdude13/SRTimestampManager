﻿// Avoid annoying warnings in Unity
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;
using SRCustomLib.Models;
using SRTimestampLib;
using SRTimestampLib.Models;

namespace SRCustomLib
{
    /// <summary>
    /// Provides map information and download via magnet torrent link
    /// </summary>
    public class CustomMapRepoTorrent
    {
        // Useful for testing, to not need to resolve the magnet file every time.
        private readonly bool CacheTorrentFile = true;
        
        /// <summary>
        /// This is used if we don't have a cached file and can't get an online one for some reason.
        /// It should be updated each release, but it'll be out of date quickly, so the main source of truth is the online magnet file.
        /// </summary>
        private string FallbackMagnetLinkHardCoded =
            "magnet:?xt=urn:btih:c2c904b7be20bb9bdcb4d2bf3b0e8dcbfba3e428&dn=CustomSongs&tr=udp%3a%2f%2ftracker.opentrackr.org%3a1337%2fannounce&tr=udp%3a%2f%2fopen.tracker.cl%3a1337%2fannounce&tr=udp%3a%2f%2ftracker.torrent.eu.org%3a451%2fannounce&tr=udp%3a%2f%2fopen.stealth.si%3a80%2fannounce&tr=udp%3a%2f%2fexplodie.org%3a6969%2fannounce&tr=udp%3a%2f%2fexodus.desync.com%3a6969%2fannounce&tr=udp%3a%2f%2ftracker.tiny-vps.com%3a6969%2fannounce&tr=udp%3a%2f%2fopen.free-tracker.ga%3a6969%2fannounce&tr=http%3a%2f%2ft.jaekr.sh%3a6969%2fannounce&tr=http%3a%2f%2fshubt.net%3a2710%2fannounce&tr=http%3a%2f%2fshare.hkg-fansub.info%3a80%2fannounce.php&tr=http%3a%2f%2fservandroidkino.ru%3a80%2fannounce&tr=http%3a%2f%2fretracker.spark-rostov.ru%3a80%2fannounce&tr=http%3a%2f%2fhome.yxgz.club%3a6969%2fannounce&tr=http%3a%2f%2ffinbytes.org%3a80%2fannounce.php&tr=http%3a%2f%2f0123456789nonexistent.com%3a80%2fannounce&tr=udp%3a%2f%2fwepzone.net%3a6969%2fannounce&tr=udp%3a%2f%2fttk2.nbaonlineservice.com%3a6969%2fannounce&tr=udp%3a%2f%2ftracker2.dler.org%3a80%2fannounce&tr=udp%3a%2f%2ftracker.tryhackx.org%3a6969%2fannounce";

        private CancellationTokenSource _cts;
        private readonly SRLogHandler _logger;
        private MagnetLink? _magnetLink;
        private readonly MagnetLinkRepo _magnetLinkRepo;
        private readonly MapMetadataRepo _mapMetadataRepo;
        private Torrent? _songTorrent = null;

        /// <summary>
        /// FileName from torrent -> the file information
        /// </summary>
        // private Dictionary<string, MapMetadata> _mapsByFileName = new();

        private ClientEngine _clientEngine;
        private TorrentManager? _songTorrentManager;
        private CustomFileManager _customFileManager;
        
        public bool IsInitialized { get; private set; }

        public CustomMapRepoTorrent(SRLogHandler logger, CustomFileManager? customFileManager = null)
        {
            _logger = logger;
            _cts = new CancellationTokenSource();

            _magnetLinkRepo = new MagnetLinkRepo(_logger);
            _mapMetadataRepo = new MapMetadataRepo(_logger);

            var engineSettings = new EngineSettingsBuilder()
            {
                CacheDirectory = FileUtils.TorrentCacheDirectory
            }.ToSettings();
            _clientEngine = new ClientEngine(engineSettings);

            _customFileManager = customFileManager ?? new CustomFileManager(_logger);
        }

        ~CustomMapRepoTorrent()
        {
            _cts?.Cancel();
        }

        private void RefreshCts(TimeSpan? timeout = null)
        {
            _cts.Cancel();

            if (timeout.HasValue)
            {
                _cts = new CancellationTokenSource(timeout.Value);
            }
            else
            {
                _cts = new CancellationTokenSource();
            }
        }

        /// <summary>
        /// Do any necessary setup/initialization (i.e. updating metadata for all songs before doing further queries).
        /// This is expected to be called before any other query method.
        /// </summary>
        /// <returns></returns>
        public async Task Initialize()
        {
            if (IsInitialized)
                return;

            await _mapMetadataRepo.Initialize();

            // Get updated link
            _magnetLink = await _magnetLinkRepo.TryGetMagnetLinkAsync() ?? MagnetLink.Parse(FallbackMagnetLinkHardCoded);

            var localTimestampMappings = await _customFileManager.GetLocalTimestampMappings();
            await RefreshMapMetadata(localTimestampMappings);

            IsInitialized = true;
        }

        private string GetFinalMapPath(string relativeFilePath)
        {
            return Path.GetFullPath(Path.Combine(FileUtils.CustomSongsPath, Path.GetFileName(relativeFilePath)));
        }

        /// <summary>
        /// Prepares the torrent manager for file downloads by excluding any unintended downloads from the manager.
        /// This indirectly updates file metadata where it is missing
        /// </summary>
        /// <param name="torrentManager"></param>
        /// <param name="includedDifficulties"></param>
        /// <param name="startTimestampSec"></param>
        /// <returns></returns>
        private async Task<List<ITorrentManagerFile>> FilterMapsForDownload(TorrentManager torrentManager, HashSet<string>? includedDifficulties = null, long startTimestampSec = 0, CancellationToken cancellationToken = default)
        {
            var toDownload = new List<ITorrentManagerFile>();
            var alreadyDownloaded = 0;
            foreach (var file in torrentManager.Files)
            {
                var fileName = Path.GetFileName(file.Path);
                var mapMetadata = await _mapMetadataRepo.GetMetadataWithFallbacks(fileName, TimeSpan.FromSeconds(10), cancellationToken);
                if (mapMetadata == null)
                {
                    _logger.ErrorLog($"No metadata found for file {fileName}; skipping");
                    continue;
                }

                // If we already have a local copy, don't download again (TODO check hash?)
                if (!string.IsNullOrEmpty(mapMetadata.DownloadedPath) && File.Exists(mapMetadata.DownloadedPath))
                {
                    // _logger.DebugLog($"Already downloaded {fileName}; skipping");
                    alreadyDownloaded++;
                    continue;
                }

                // TODO account for file updates w/ same file name

                // Check time. If there's no time, then assume it's newer than the site or cached list, so should be downloaded
                if (mapMetadata.PublishedAtTimestampSec > 0 && mapMetadata.PublishedAtTimestampSec < startTimestampSec)
                {
                    // _logger.DebugLog($"Published time {mapMetadata.PublishedAtTimestampSec} < {startTimestampSec}; skipping");
                    continue;
                }

                // Check difficulty
                if (includedDifficulties != null)
                {
                    if (mapMetadata.SupportedDifficulties == null)
                    {
                        _logger.DebugLog("No difficulties available in metadata; skipping song");
                        continue;
                    }
                    
                    var diffIsIncluded = false;
                    foreach (var diff in mapMetadata.SupportedDifficulties)
                    {
                        if (includedDifficulties.Contains(diff))
                        {
                            diffIsIncluded = true;
                            break;
                        }
                    }

                    if (!diffIsIncluded)
                    {
                        _logger.DebugLog("Ignoring song; matching difficulty not found");
                        continue;
                    }
                }

                // Not excluded; go ahead with download
                _logger.DebugLog($"Marking {fileName} for download");
                await torrentManager.SetFilePriorityAsync(file, Priority.Normal);
                toDownload.Add(file);
            }

            _logger.DebugLog($"Already downloaded {alreadyDownloaded} maps; skipping those");

            await _mapMetadataRepo.PersistCache();

            return toDownload;
        }

        /// <summary>
        /// Downloads the map with the given filename if it is present in the torrent
        /// </summary>
        /// <param name="fileName">The name of the resulting file, including extension (.synth)</param>
        /// <returns>Path to the downloaded file, or null if failed</returns>
        public async Task<string?> DownloadMapFromFilename(string fileName)
        {
            // Prep for download
            var manager = await GetManagerAllDoNotDownload();
            if (manager == null)
                return null;

            // Select the file for download
            List<ITorrentManagerFile> toDownload = new();
            foreach (var file in manager.Files)
            {
                if (Path.GetFileName(file.Path) == fileName)
                {
                    await manager.SetFilePriorityAsync(file, Priority.Normal);
                    toDownload.Add(file);
                    break;
                }
            }

            // Download
            var downloaded = await DownloadInternal(manager, toDownload);
            if (downloaded == null || downloaded.Count != 1)
            {
                _logger.ErrorLog("Download failed");
                return null;
            }

            return downloaded[0].FilePath;
        }

        /// <summary>
        /// Does the actual downloading of the given files, and updates any necessary internal state with their contents
        /// </summary>
        /// <param name="manager"></param>
        /// <param name="toDownload"></param>
        /// <returns></returns>
        private async Task<List<MapZMetadata>?> DownloadInternal(TorrentManager manager, List<ITorrentManagerFile> toDownload)
        {
            List<MapZMetadata> downloadedMaps = new List<MapZMetadata>();
            
            _logger.DebugLog($"Starting {toDownload.Count} download(s)...");
            RefreshCts(TimeSpan.FromHours(1));
            var success = await DownloadAllSync(manager, _cts.Token);
            if (!success)
            {
                _logger.ErrorLog("Download failed!");
                return null;
            }

            _logger.DebugLog("Done downloading. Moving files...");
            var filesMoved = 0;
            var filesSkipped = 0;
            var filesDeleted = 0;
            foreach (var file in toDownload)
            {
                if (File.Exists(file.DownloadCompleteFullPath))
                {
                    var parsed = await CustomFileManager.ParseLocalMap(file.DownloadCompleteFullPath, _logger);
                    if (parsed == null)
                    {
                        _logger.ErrorLog($"Failed to parse at {file.DownloadCompleteFullPath}; deleting and skipping");
                        FileUtils.DeleteFile(file.DownloadCompleteFullPath, _logger);
                        filesDeleted++;
                        continue;
                    }

                    var targetPath = GetFinalMapPath(file.Path);
                    if (!FileUtils.MoveFileOverwrite(file.DownloadCompleteFullPath, targetPath, _logger))
                    {
                        _logger.ErrorLog(
                            $"Failed to move file {file.DownloadCompleteFullPath} to custom songs dir! Skipping");
                        filesSkipped++;
                        continue;
                    }

                    parsed.FilePath = targetPath;
                    downloadedMaps.Add(parsed);

                    filesMoved++;
                }
                else if (File.Exists(file.DownloadIncompleteFullPath))
                {
                    _logger.ErrorLog($"Incomplete download at {file.DownloadIncompleteFullPath}; deleting");
                    filesDeleted++;
                    FileUtils.DeleteFile(file.DownloadIncompleteFullPath, _logger);
                }
                else
                {
                    _logger.ErrorLog($"File {file.Path} download failed (not found); skipping");
                    filesSkipped++;
                }
            }

            // TODO delete empty files?

            _logger.DebugLog($"{filesMoved} files moved, {filesDeleted} deleted, {filesSkipped} skipped");
            
            // Only bother with imports and db updates if there were actually any new songs updated
            if (downloadedMaps.Count > 0)
            {
                // await _customFileManager.AddLocalMaps(downloadedMaps.Select(mapMetadata => mapMetadata.FilePath).ToList());
                var numProcessed = 0;
                foreach (var map in downloadedMaps)
                {
                    await _customFileManager.AddLocalMap(map.FilePath, null);

                    numProcessed++;
                    if (numProcessed % 10 == 0)
                    {
                        await Task.Yield();
                    }
                }
                await _customFileManager.db.Save();
        
                // Might as well fix the timestamps while we're here :)
                await _customFileManager.ApplyLocalMappings(await _customFileManager.GetLocalTimestampMappings());
        
                // Update the actual SR database as well, for faster game import (and ensured accuracy)
                await _customFileManager.UpdateSynthDBTimestamps();            
            }

            return downloadedMaps;
        }

        /// <summary>
        /// Downloads all songs, with the given filters.
        /// </summary>
        /// <param name="includedDifficulties">If set, only download songs that have one of the included difficulties. If null, download all difficulties.</param>
        /// <param name="startTime">Only maps published after this time will be included. Default is 0, so all maps.</param>
        /// <returns>Downloaded map data, or empty list if none found. Returns null on error.</returns>
        public async Task<List<MapZMetadata>?> DownloadMaps(HashSet<string>? includedDifficulties = null, DateTimeOffset startTime = default, CancellationToken cancellationToken = default)
        {
            // Prep for download
            var manager = await GetManagerAllDoNotDownload();
            if (manager == null)
                return null;

            _logger.DebugLog("Filtering maps...");

            // Filter. All start as DoNotDownload, so just enable valid maps
            var startTimestampSec = startTime.ToUnixTimeSeconds();
            var toDownload = await FilterMapsForDownload(manager, includedDifficulties, startTimestampSec, cancellationToken);

            // Do the actual downloading
            if (toDownload.Count == 0)
            {
                _logger.DebugLog("No maps marked for download; up-to-date");
                return new List<MapZMetadata>();
            }

            return await DownloadInternal(manager, toDownload);
        }

        /// <summary>
        /// Downloads all songs that are marked for download, and waits for all downloads to finish
        /// </summary>
        /// <param name="manager"></param>
        /// <param name="token"></param>
        private async Task<bool> DownloadAllSync(TorrentManager manager, CancellationToken token)
        {
            var downloadCompleted = new TaskCompletionSource<bool>();
            // Canceled token cancels the download
            await using var registration = token.Register(() =>
            {
                _logger.DebugLog("Cancelled, setting false");
                downloadCompleted.SetResult(false);
            });

            _logger.DebugLog("Starting download...");
            await manager.StartAsync();

            _logger.DebugLog("Waiting for download...");
            var lastProgress = -1.0;
            while (!downloadCompleted.Task.IsCompleted)
            {
                if (manager.State == TorrentState.Downloading && Math.Abs(manager.PartialProgress - lastProgress) >= 1f)
                {
                    _logger.DebugLog($"  Progress: {(int)manager.PartialProgress}%");
                    lastProgress = manager.PartialProgress;
                }
                
                if (manager.State == TorrentState.Error)
                {
                    _logger.ErrorLog($"Error while downloading! {manager.Error}");
                    downloadCompleted.SetResult(false);
                }

                if (manager.State == TorrentState.Stopped)
                {
                    _logger.DebugLog($"Stopped. Partial progress is {manager.PartialProgress}");
                    if (manager.PartialProgress >= 100)
                    {
                        downloadCompleted.SetResult(true);
                    }
                    else
                    {
                        downloadCompleted.SetResult(false);
                    }
                }

                if (manager.State == TorrentState.Seeding)
                {
                    // Must be done if we're here
                    _logger.DebugLog("Seeding! Marking as done to stop");
                    downloadCompleted.SetResult(true);
                }

                await Task.Delay(1000, token);
            }

            var isSuccess = downloadCompleted.Task.IsCompletedSuccessfully && downloadCompleted.Task.Result;

            if (!isSuccess)
            {
                _logger.ErrorLog("Failed download!");
                return false;
            }

            // Stop once we finish, to avoid seeding
            _logger.DebugLog("Stopping download...");
            await manager.StopAsync();
            _logger.DebugLog("Stopped");
            await _clientEngine.RemoveAsync(manager, RemoveMode.KeepAllData);

            return true;
        }

        private async Task<Torrent?> GetTorrent()
        {
            if (_songTorrent == null)
            {
                // Try from cache first
                if (CacheTorrentFile)
                {
                    _songTorrent = GetTorrentFromCache();
                }
                
                // Get from mag link if not found
                if (_magnetLink != null)
                {
                    _songTorrent ??= await GetTorrentFromMagLink(_magnetLink);
                }
            }
            
            return _songTorrent;
        }

        /// <summary>
        /// Gets the latest map list from the magnet link
        /// </summary>
        /// <returns></returns>
        private async Task RefreshMapMetadata(LocalMapTimestampMappings localTimestampMappings)
        {
            _logger.DebugLog("Refreshing map metadata...");
            var torrent = await GetTorrent();
            if (torrent == null)
                return;

            // Get all files found within the latest torrent
            // _mapsByFileName.Clear();
            var files = torrent.Files;

            var numFiles = files.Count;
            _logger.DebugLog($"Found {numFiles} files in torrent");
            for (var i = 0; i < numFiles; i++)
            {
                var file = files[i];

                var trimmedName = file.Path.Trim();
                if (!trimmedName.EndsWith(".synth"))
                {
                    _logger.DebugLog($"Skipping non-map file {file.Path}");
                    continue;
                }

                var metadata = new MapMetadata
                {
                    // TODO get more metadata from newer maps, correlate file names with metadata
                    FileName = trimmedName,
                    DownloadedPath = GetFinalMapPath(trimmedName),
                    PublishedAtTimestampSec = 0
                };

                if (localTimestampMappings.TryGetPublishedAtForFilename(trimmedName, out var publishedAt))
                {
                    metadata.PublishedAtTimestampSec = (int)(publishedAt - DateTime.UnixEpoch).TotalSeconds;
                }

                // _logger.DebugLog($"File: {file.Path}");
                // _mapsByFileName[trimmedName] = metadata;
                _mapMetadataRepo.AddToCache(metadata);

                if (i % 500 == 0)
                {
                    _logger.DebugLog($"Processed {i}/{numFiles}");
                }
            }

            _logger.DebugLog($"Processed {numFiles}/{numFiles}");
            await _mapMetadataRepo.PersistCache();

            _logger.DebugLog($"Done processing files");
        }

        /// <summary>
        /// Creates a fresh TorrentManager from the torrent, initialized with no files set to download.
        /// </summary>
        /// <returns></returns>
        private async Task<TorrentManager?> GetManagerAllDoNotDownload()
        {
            if (_songTorrentManager != null)
            {
                await _clientEngine.RemoveAsync(_songTorrentManager);
            }

            var torrent = GetTorrentFromCache() ?? await GetTorrentFromMagLink(_magnetLink!);
            if (torrent == null)
                return null;

            _songTorrentManager = await _clientEngine.AddAsync(torrent, FileUtils.TorrentDownloadDirectory);
            
            _logger.DebugLog($"Torrent has {_songTorrentManager.Files.Count} files");

            // Start with no files downloaded
            _logger.DebugLog("Unselecting all files to start...");
            var numProcessed = 0;
            var numFiles = _songTorrentManager.Files.Count;
            foreach (var file in _songTorrentManager.Files)
            {
                await _songTorrentManager.SetFilePriorityAsync(file, Priority.DoNotDownload);
                numProcessed++;

                if (numProcessed % 100 == 0)
                {
                    _logger.DebugLog($"  Processed {numProcessed} / {numFiles} files");
                }
            }

            _logger.DebugLog($"  Processed {numFiles} / {numFiles} files");
            _logger.DebugLog("TorrentManager created with all maps set to DoNotDownload");
            return _songTorrentManager;
        }

        private async Task CacheTorrentAsync(byte[] torrentBytes)
        {
            var torrentCacheLocation = FileUtils.CachedTorrentFile;
            await FileUtils.WriteToFile(torrentBytes, torrentCacheLocation, _logger);
        }

        private Torrent? GetTorrentFromCache()
        {
            var torrentCacheLocation = FileUtils.CachedTorrentFile;
            if (Torrent.TryLoad(torrentCacheLocation, out Torrent? torrent))
                return torrent;

            return null;
        }

        /// <summary>
        /// Gets the Torrent from the mag link, doing necessary parsing
        /// </summary>
        /// <param name="magLink"></param>
        /// <returns></returns>
        private async Task<Torrent?> GetTorrentFromMagLink(MagnetLink magLink)
        {
            RefreshCts(TimeSpan.FromSeconds(300));

            try
            {
                _logger.DebugLog("Getting torrent from magnet link (this can take a few minutes)...");
                var torrentFileBytes = await _clientEngine.DownloadMetadataAsync(magLink, _cts.Token);
                _logger.DebugLog("Parsing torrent...");
                var writeableTorrentBytes = torrentFileBytes.ToArray();
                var torrent = await Torrent.LoadAsync(writeableTorrentBytes);

                if (CacheTorrentFile)
                {
                    await CacheTorrentAsync(writeableTorrentBytes);
                }

                return torrent;
            }
            catch (OperationCanceledException ex)
            {
                _logger.ErrorLog($"Failed to get torrent from magnet link (timed out): {ex.Message}");
                return null;
            }
        }
    }
}