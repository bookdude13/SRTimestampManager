using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SRTimestampLib.Models;

// Avoid annoying warnings in Unity
#nullable enable

namespace SRTimestampLib
{
    [Serializable]
    // TODO add locking/singleton, and/or tests
    public class LocalDatabase
    {
        [JsonIgnore]
        private readonly string LOCAL_DATABASE_NAME = "SRQD_local.db";
        [JsonIgnore]
        private SRLogHandler logger;

        [JsonProperty]
        private List<MapZMetadata> localMapMetadata = new();

        /// <summary>
        /// Keep track of the last full fetch we did, to allow incremental updates.
        /// </summary>
        [JsonProperty]
        public long LastFetchTimestampSec { get; private set; }
        
        public List<MapZMetadata> GetLocalMapsCopy() => new(localMapMetadata);

        /// Faster lookup of maps by path
        [JsonIgnore]
        private Dictionary<string, MapZMetadata> localMapPathLookup = new();

        /// Faster lookup of maps by hash
        [JsonIgnore]
        private Dictionary<string, MapZMetadata> localMapHashLookup = new();

        [JsonIgnore]
        private bool _isDirty;
        
        [JsonIgnore]
        public readonly string Id;

        // public LocalDatabase() { }

        public LocalDatabase(SRLogHandler logger)
        {
            this.logger = logger;
            Id = Guid.NewGuid().ToString();
        }

        /// Gets locally stored metadata based on file path.
        /// Returns null if not found
        public MapZMetadata? GetFromPath(string filePath)
        {
            if (localMapPathLookup.ContainsKey(filePath))
            {
                return localMapPathLookup[filePath];
            }

            return null;
        }

        /// Gets locally stored metadata based on map hash.
        /// Returns null if not found
        public MapZMetadata? GetFromHash(string hash)
        {
            if (localMapHashLookup.ContainsKey(hash))
            {
                return localMapHashLookup[hash];
            }

            return null;
        }

        public int GetNumberOfMaps()
        {
            return localMapMetadata.Count;
        }

        /// Adds map metadata to database.
        /// If the file path is already present or hash is already present replace
        public void AddMap(MapZMetadata mapMeta, SRLogHandler logger)
        {
            // Remove existing to replace with new
            if (localMapPathLookup.ContainsKey(mapMeta.FilePath))
            {
                logger.DebugLog($"Removing map with existing path {mapMeta.FilePath}");
                localMapMetadata.Remove(localMapPathLookup[mapMeta.FilePath]);
                localMapPathLookup.Remove(mapMeta.FilePath);
            }

            if (localMapHashLookup.ContainsKey(mapMeta.hash))
            {
                logger.DebugLog($"Removing map with matching hash {mapMeta.hash}");
                localMapMetadata.Remove(localMapHashLookup[mapMeta.hash]);
                localMapHashLookup.Remove(mapMeta.hash);
            }

            logger.DebugLog($"Adding map {Path.GetFileNameWithoutExtension(mapMeta.FilePath)}");
            localMapPathLookup.Add(mapMeta.FilePath, mapMeta);
            localMapHashLookup.Add(mapMeta.hash, mapMeta);
            localMapMetadata.Add(mapMeta);

            _isDirty = true;
        }

        /// Remove maps that aren't in the list of hashes
        public void RemoveMissingHashes(HashSet<string> savedHashes)
        {
            var toRemove = new List<MapZMetadata>();
            foreach (var mapMeta in localMapMetadata)
            {
                if (!savedHashes.Contains(mapMeta.hash))
                {
                    // Not saved; remove from db
                    toRemove.Add(mapMeta);
                }
            }

            foreach (var mapMeta in toRemove)
            {
                logger.DebugLog($"db map not found in filesystem; removing {Path.GetFileName(mapMeta.FilePath)}");
                localMapMetadata.Remove(mapMeta);
                localMapPathLookup.Remove(mapMeta.FilePath);
                localMapHashLookup.Remove(mapMeta.hash);
            }

            _isDirty = toRemove.Count > 0;
        }

        /// Loads db state from file.
        /// Note: Not done implicitly upon creation!
        public async Task Load()
        {
            if (!File.Exists(GetDbPath()))
            {
                logger.DebugLog("DB doesn't exist; creating...");
                await Save(true);
            };

            logger.DebugLog("Loading database...");
            LocalDatabase? localDb = await FileUtils.ReadFileJson<LocalDatabase>(GetDbPath(), logger);
            if (localDb == null)
            {
                logger.ErrorLog("Failed to load local database!");
                return;
            }

            // Copy fields
            this.localMapMetadata = localDb.localMapMetadata;
            this.LastFetchTimestampSec = localDb.LastFetchTimestampSec;

            this.localMapPathLookup.Clear();
            this.localMapHashLookup.Clear();
            foreach (var mapMeta in localMapMetadata)
            {
                localMapPathLookup.Add(mapMeta.FilePath, mapMeta);
                localMapHashLookup.Add(mapMeta.hash, mapMeta);
            }
            logger.DebugLog("DB loaded");
        }

        /// Saves db state to file
        /// Returns true if successful, false if not
        public async Task<bool> Save(bool force = false)
        {
            if (!force && !_isDirty)
            {
                logger.DebugLog("Skipping save (not dirty)");
                return true;
            }

            _isDirty = false;

            try
            {
                string asJson = JsonConvert.SerializeObject(this, Formatting.Indented);
                string tempFile = GetTempFilePath();
                logger.DebugLog($"Saving db {Id} ({localMapMetadata.Count} maps)");
                if (!await FileUtils.WriteToFile(asJson, tempFile, logger))
                {
                    logger.ErrorLog("Failed to write db to temp file");
                    return false;
                }

                return FileUtils.MoveFileOverwrite(tempFile, GetDbPath(), logger);
            }
            catch (System.Exception e)
            {
                logger.ErrorLog("Failed to save db: " + e.Message);
                return false;
            }
        }

        private string GetTempFilePath()
        {
            return Path.Join(FileUtils.TempPath, Guid.NewGuid().ToString());
        }

        private string GetDbPath()
        {
            return Path.Join(FileUtils.GetPersistentFolder(), LOCAL_DATABASE_NAME);
        }
        
        public void SetLastDownloadedTime(DateTime lastDownloadedTime) {
            DateTimeOffset dto = lastDownloadedTime;
            LastFetchTimestampSec = (int)dto.ToUnixTimeSeconds();
            _isDirty = true;
        }

        public DateTime GetLastDownloadedTime() => DateTimeOffset.FromUnixTimeSeconds(LastFetchTimestampSec).UtcDateTime;
    }
}