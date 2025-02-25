﻿using MonoTorrent;
using MonoTorrent.Client;
using SRCustomLib.Models;
using SRTimestampLib;
using SRTimestampLib.Models;

namespace SRCustomLib;

/// <summary>
/// Provides map information and download via magnet torrent link
/// </summary>
public class CustomMapRepoTorrent
{
    // Useful for testing, to not need to resolve the magnet file every time.
    private readonly bool CacheTorrentFile = true;
    
    private string _magnetLink = "";
    private CancellationTokenSource _cts;
    private SRLogHandler _logger;
    
    /// <summary>
    /// FileName from torrent -> the file information
    /// </summary>
    private Dictionary<string, MapMetadata> _mapsByFileName = new();

    private ClientEngine _clientEngine;
    private Torrent? _songTorrent;
    private CustomFileManager _customFileManager;
    
    public CustomMapRepoTorrent()
    {
        _logger = new SRLogHandler();
        _cts = new CancellationTokenSource();

        var engineSettings = new EngineSettingsBuilder()
        {
            CacheDirectory = FileUtils.TorrentCacheDirectory
        }.ToSettings();
        _clientEngine = new ClientEngine(engineSettings);

        _customFileManager = new CustomFileManager(_logger);
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
        // TODO API call to get latest magnet link, or hosted in github or something
        _magnetLink = MagnetLinkHardCoded;

        var localTimestampMappings = _customFileManager.GetLocalTimestampMappings();
        _songTorrent = await RefreshMapMetadata(_magnetLink, localTimestampMappings);
    }

    /// <summary>
    /// Downloads all songs, with the given filters.
    /// </summary>
    /// <param name="includedDifficulties">If set, only download songs that have one of the included difficulties. If null, download all difficulties.</param>
    /// <param name="startTimestampSec">Only maps published after this timestamp will be included. Default is 0, so all maps.</param>
    /// <returns></returns>
    public async Task<List<MapZMetadata>> DownloadMaps(HashSet<string>? includedDifficulties = null, long startTimestampSec = 0)
    {
        var downloadedMaps = new List<MapZMetadata>();

        // Prep for download
        var manager = await GetManagerAllDoNotDownload();
        if (manager == null)
            return new List<MapZMetadata>();

        _logger.DebugLog("Filtering maps...");

        // Filter. All start as DoNotDownload, so just enable valid maps
        var toDownload = new List<ITorrentManagerFile>();
        foreach (var file in manager.Files)
        {
            if (!_mapsByFileName.TryGetValue(file.Path, out var mapMetadata))
            {
                _logger.DebugLog($"No metadata for file {file.Path}; skipping");
                continue;
            }
            
            // TODO If we already have a local copy, don't download again (TODO check hash?)
            // Not doing now, to account for file updates w/ same file name

            // Check time
            if (mapMetadata.PublishedAtTimestampSec < startTimestampSec)
                continue;

            // Check difficulty
            if (includedDifficulties != null)
            {
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
                    continue;
            }
            
            // Not excluded; go ahead with download
            _logger.DebugLog($"Marking {file.Path} for download");
            await manager.SetFilePriorityAsync(file, Priority.Normal);
            toDownload.Add(file);
        }
        
        // Do the actual downloading
        if (toDownload.Count == 0)
        {
            _logger.DebugLog("No maps marked for download; up-to-date");
            return downloadedMaps;
        }

        _logger.DebugLog($"Starting {toDownload.Count} download(s)...");
        var success = await DownloadAllSync(manager, _cts.Token);
        if (!success)
        {
            _logger.ErrorLog("Download failed!");
            return new List<MapZMetadata>();
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
                
                var targetPath = Path.Combine(file.DownloadCompleteFullPath, Path.GetFullPath(Path.Combine(FileUtils.CustomSongsPath, Path.GetFileName(file.Path))));
                // _logger.DebugLog($"File {file.Path} downloaded successfully. Moving to CustomSongs...");
                if (!FileUtils.MoveFileOverwrite(file.DownloadCompleteFullPath, targetPath, _logger))
                {
                    _logger.ErrorLog($"Failed to move file {file.DownloadCompleteFullPath} to custom songs dir! Skipping");
                    filesSkipped++;
                    continue;
                }

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
        _logger.DebugLog($"{filesMoved} files moved, {filesDeleted} deleted, {filesSkipped} skipped");

        return downloadedMaps;
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
        await using var registration = token.Register (() =>
        {
            _logger.DebugLog("Cancelled, setting false");
            downloadCompleted.SetResult(false);
        });

        manager.TorrentStateChanged += ManagerOnTorrentStateChanged;
        void ManagerOnTorrentStateChanged(object? sender, TorrentStateChangedEventArgs e)
        {
            _logger.DebugLog($"Changing state from {e.OldState} to {e.NewState}");
            
            if (e.NewState == TorrentState.Error)
            {
                _logger.ErrorLog($"Error while downloading! {e.TorrentManager.Error}");
                downloadCompleted.SetResult(false);
            }

            if (e.NewState == TorrentState.Stopped)
            {
                _logger.DebugLog($"Stopped. Partial progress is {e.TorrentManager.PartialProgress}");
                if (e.TorrentManager.PartialProgress >= 100)
                {
                    downloadCompleted.SetResult(true);
                }
                else
                {
                    downloadCompleted.SetResult(false);
                }
            }

            if (e.NewState == TorrentState.Seeding)
            {
                // Must be done if we're here
                _logger.DebugLog("Seeding! Marking as done to stop");
                downloadCompleted.SetResult(true);
            }
        }

        _logger.DebugLog("Starting download...");
        await manager.StartAsync();
        
        _logger.DebugLog("Waiting for download...");
        var lastProgress = -1.0;
        while (!downloadCompleted.Task.IsCompleted)
        {
            if (manager.State == TorrentState.Downloading && Math.Abs(manager.PartialProgress - lastProgress) > 0.01f)
            {
                _logger.DebugLog($"  Progress: {(int)manager.PartialProgress}%");
                lastProgress = manager.PartialProgress;
            }
            await Task.Delay(1000, token);
        }

        var isSuccess = downloadCompleted.Task.IsCompletedSuccessfully && downloadCompleted.Task.Result;
        if (!isSuccess)
        {
            _logger.ErrorLog("Failed download!");
            return false;
        }
        
        manager.TorrentStateChanged -= ManagerOnTorrentStateChanged;
        
        // Stop once we finish, to avoid seeding
        _logger.DebugLog("Stopping download...");
        await manager.StopAsync();
        _logger.DebugLog("Stopped");

        return true;
    }

    /// <summary>
    /// Gets the latest map list from the magnet link
    /// </summary>
    /// <returns></returns>
    private async Task<Torrent?> RefreshMapMetadata(string magnetLink, LocalMapTimestampMappings localTimestampMappings)
    {
        _logger.DebugLog("Refreshing map metadata...");
        Torrent? torrent = null;
        if (CacheTorrentFile)
        {
            torrent = await GetTorrentFromCache();
        }
        torrent ??= await GetTorrentFromMagLink(magnetLink);
        if (torrent == null)
            return null;
        
        // Get all files found within the latest torrent
        _mapsByFileName.Clear();
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
                FileName = trimmedName
            };
            
            if (localTimestampMappings.TryGetPublishedAtForFilename(trimmedName, out var publishedAt))
            {
                metadata.PublishedAtTimestampSec = (int)(publishedAt - DateTime.UnixEpoch).TotalSeconds;
            }
            
            // _logger.DebugLog($"File: {file.Path}");
            _mapsByFileName[trimmedName] = metadata;
        
            if (i % 500 == 0)
            {
                _logger.DebugLog($"Processed {i}/{numFiles}");
            }
        }
        
        _logger.DebugLog($"Processed {numFiles}/{numFiles}");
        _logger.DebugLog($"Done processing files. Found {_mapsByFileName.Count} maps");
        return torrent;
    }

    /// <summary>
    /// Creates a fresh TorrentManager from the torrent, initialized with no files set to download.
    /// </summary>
    /// <returns></returns>
    private async Task<TorrentManager?> GetManagerAllDoNotDownload()
    {
        var torrent = _songTorrent ?? await GetTorrentFromMagLink(_magnetLink);
        if (torrent == null)
            return null;
        
        var manager = await _clientEngine.AddAsync(torrent, FileUtils.TorrentDownloadDirectory);
        
        _logger.DebugLog($"Torrent has {manager.Files.Count} files");
        
        // Start with no files downloaded
        foreach (var file in manager.Files)
        {
            await manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
        }
        _logger.DebugLog("TorrentManager created with all maps set to DoNotDownload");
        return manager;
    }

    private async Task CacheTorrentAsync(byte[] torrentBytes)
    {
        var torrentCacheLocation = FileUtils.CachedTorrentFile;
        await FileUtils.WriteToFile(torrentBytes, torrentCacheLocation, _logger);
    }

    private async Task<Torrent?> GetTorrentFromCache()
    {
        var torrentCacheLocation = FileUtils.CachedTorrentFile;
        if (Torrent.TryLoad(torrentCacheLocation, out Torrent? torrent))
            return torrent;

        return null;
    }

    /// <summary>
    /// Gets the Torrent from the mag link, doing necessary parsing
    /// </summary>
    /// <param name="magnetLink"></param>
    /// <returns></returns>
    private async Task<Torrent?> GetTorrentFromMagLink(string magnetLink)
    {
        RefreshCts(TimeSpan.FromSeconds(30));
        var magLink = MagnetLink.Parse(magnetLink);

        try
        {
            _logger.DebugLog("Getting torrent from magnet link...");
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

    

    private string MagnetLinkHardCoded = "magnet:?xt=urn:btih:c2c904b7be20bb9bdcb4d2bf3b0e8dcbfba3e428&dn=CustomSongs&tr=udp%3a%2f%2ftracker.opentrackr.org%3a1337%2fannounce&tr=udp%3a%2f%2fopen.tracker.cl%3a1337%2fannounce&tr=udp%3a%2f%2ftracker.torrent.eu.org%3a451%2fannounce&tr=udp%3a%2f%2fopen.stealth.si%3a80%2fannounce&tr=udp%3a%2f%2fexplodie.org%3a6969%2fannounce&tr=udp%3a%2f%2fexodus.desync.com%3a6969%2fannounce&tr=udp%3a%2f%2ftracker.tiny-vps.com%3a6969%2fannounce&tr=udp%3a%2f%2fopen.free-tracker.ga%3a6969%2fannounce&tr=http%3a%2f%2ft.jaekr.sh%3a6969%2fannounce&tr=http%3a%2f%2fshubt.net%3a2710%2fannounce&tr=http%3a%2f%2fshare.hkg-fansub.info%3a80%2fannounce.php&tr=http%3a%2f%2fservandroidkino.ru%3a80%2fannounce&tr=http%3a%2f%2fretracker.spark-rostov.ru%3a80%2fannounce&tr=http%3a%2f%2fhome.yxgz.club%3a6969%2fannounce&tr=http%3a%2f%2ffinbytes.org%3a80%2fannounce.php&tr=http%3a%2f%2f0123456789nonexistent.com%3a80%2fannounce&tr=udp%3a%2f%2fwepzone.net%3a6969%2fannounce&tr=udp%3a%2f%2fttk2.nbaonlineservice.com%3a6969%2fannounce&tr=udp%3a%2f%2ftracker2.dler.org%3a80%2fannounce&tr=udp%3a%2f%2ftracker.tryhackx.org%3a6969%2fannounce";
}