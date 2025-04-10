using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using MemoryPack;
using Newtonsoft.Json;
using SRCustomLib.Models;
using SRTimestampLib;
using SRTimestampLib.Models;
using Debug = SRTimestampLib.Debug;

// Avoid annoying warnings in Unity
#nullable enable

namespace SRCustomLib
{
    /// <summary>
    /// Handles retrieving the latest metadata for maps
    /// </summary>
    public class MapMetadataRepo
    {
        private const string GET_MAP_METADATA_URL_Z = "https://synthriderz.com/api/beatmaps";
        private const string GET_MAP_METADATA_URL_SYN = "https://api.synplicity.live/beatmaps";
        private const string METADATA_CACHE_FILE = "map_metadata.memorypack";

        private const bool USE_Z = true;
        private const bool USE_SYN = false;

        private readonly SRLogHandler _logger;
        private readonly HttpClient _client = new();

        private CachedMapMetadata _cachedMapMetadata = new();
        
        private bool _hasInitialized;
        private bool _isDirty;
        
        private string CachePath => Path.Combine(FileUtils.GetPersistentFolder(), METADATA_CACHE_FILE);

        public MapMetadataRepo(SRLogHandler logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Adds the metadata to the cache, optionally overwriting an existing entry
        /// </summary>
        /// <param name="metadata"></param>
        /// <param name="overwriteExisting"></param>
        public void AddToCache(MapMetadata metadata, bool overwriteExisting = false)
        {
            // TODO consider merging instead?
            var trimmedName = metadata.FileName!.Trim();
            if (overwriteExisting || !_cachedMapMetadata.MetadataByFileName.ContainsKey(trimmedName))
            {
                _cachedMapMetadata.MetadataByFileName[trimmedName] = metadata;
                _isDirty = true;
            }
        }

        /// <summary>
        /// Saves the current state of the cache so it can be loaded on a new run.
        /// </summary>
        public async Task PersistCache()
        {
            // TODO better cache

            if (_isDirty)
            {
                _isDirty = false;
                _logger.DebugLog("Persisting map metadata...");

                await FileUtils.WriteToFileMemoryPacked(_cachedMapMetadata, CachePath, _logger);
            }
        }

        public async Task Initialize()
        {
            if (_hasInitialized)
            {
                return;
            }
            
            _logger.DebugLog("Loading map metadata...");

            _cachedMapMetadata = await FileUtils.ReadFileMemoryPack<CachedMapMetadata>(CachePath, _logger) ?? new();
            
            _hasInitialized = true;
        }

        /// <summary>
        /// Searches cache, then Z, then Syn, for metadata for the map with the given fileName
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public async Task<MapMetadata?> GetMetadataWithFallbacks(string fileName, TimeSpan timeout)
        {
            // Check for PublishedAt being 0, in cases of local files having partial metadata
            if (_cachedMapMetadata.MetadataByFileName.TryGetValue(fileName, out var cachedMetadata) && cachedMetadata.PublishedAtTimestampSec > 0)
            {
                return cachedMetadata;
            }
            
            MapMetadata? metadata = null;
            var escapedFileName = Uri.EscapeDataString(fileName);
            
                // First, try for Z
                if (USE_Z)
                {
                    try
                    {
                        string request = GET_MAP_METADATA_URL_Z + "?s={\"filename\":\"" + escapedFileName + "\"}";
                        var requestUri = new Uri(request);
                        var timeoutCancelCts = new CancellationTokenSource(timeout);
                        string? rawResult = await _client.GetStringAsync(requestUri, timeoutCancelCts.Token);

                        MapPage? metadataList = string.IsNullOrEmpty(rawResult) ? null : JsonConvert.DeserializeObject<MapPage>(rawResult);
                        if (metadataList != null && metadataList.data.Count == 1)
                        {
                            metadata = MapMetadata.FromMapItem(metadataList.data[0]);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.ErrorLog($"Failed to get metadata from Z for map '{fileName}': {e.Message}");
                    }
                }

                if (metadata == null && USE_SYN)
                {
                    try
                    {
                        // Fallback on Syn, once that's up and functional
                        var request = $"{GET_MAP_METADATA_URL_SYN}/{escapedFileName}";
                        var requestUri = new Uri(request);
                        var timeoutCancelCts = new CancellationTokenSource(timeout);
                        var rawResult = await _client.GetStringAsync(requestUri, timeoutCancelCts.Token);
                        metadata = string.IsNullOrEmpty(rawResult) ? null : JsonConvert.DeserializeObject<MapMetadata>(rawResult);
                    }
                    catch (Exception e)
                    {
                        _logger.ErrorLog($"Failed to get metadata from Syn for map '{fileName}': {e.Message}");
                    }
                }
                
                // If we found metadata, cache it for later
                if (metadata != null)
                {
                    AddToCache(metadata, true);
                }

                // If we didn't get full metadata but had cached metadata, use that since it might be partial
                return metadata ?? cachedMetadata;
        }
    }
}