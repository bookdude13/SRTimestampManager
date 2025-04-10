using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite;
using SRTimestampLib.Models;

// Avoid annoying warnings in Unity
#nullable enable

namespace SRTimestampLib
{
    public class CustomFileManager
    {
        public SRLogHandler logger;
        public LocalDatabase db;

        private readonly string MAP_EXTENSION = ".synth";
        private readonly HashSet<string> STAGE_EXTENSIONS = new()
        {
            ".stagequest", // Old quest stages, still used for Q1 and Pico
            ".spinstagequest", // Old quest spin stages, still used for Q1 and Pico
            ".stagedroid" // Q2+ stage files, used for both spin and non-spin stages
        };
        private readonly string PLAYLIST_EXTENSION = ".playlist";

        public CustomFileManager(SRLogHandler logger)
        {
            this.logger = logger;
            db = new LocalDatabase(logger);
        }

        public async Task Initialize()
        {
            await RefreshLocalDatabase();
        }

        /// Parses the map at the given path and adds it to the collection
        public async Task AddLocalMap(string mapPath, MapItem? mapFromZ)
        {
            var metadata = await ParseLocalMap(mapPath, logger, mapFromZ);
            if (metadata == null)
            {
                logger.ErrorLog("Failed to parse map " + Path.GetFileNameWithoutExtension(mapPath));
                return;
            }

            db.AddMap(metadata, logger);
        }

        public async Task AddLocalMaps(List<string> mapPaths)
        {
            var numProcessed = 0;
            foreach (var mapPath in mapPaths)
            {
                await AddLocalMap(mapPath, null);

                numProcessed++;
                if (numProcessed % 10 == 0)
                {
                    await Task.Yield();
                }
            }
            await db.Save();
        }

        public string[] GetCustomMapPaths() => GetSynthriderzMapFiles(FileUtils.CustomSongsPath);

        /// Returns list of all maps downloaded from synthriderz.com located in the given directory.
        /// If none found or error occurs, returns empty array
        public string[] GetSynthriderzMapFiles(string rootDirectory)
        {
            try
            {
                var directoryExists = Directory.Exists(rootDirectory);
                logger.DebugLog($"Getting map files from {rootDirectory}. Directory exists? {directoryExists}");
                if (directoryExists)
                {
                    return Directory.GetFiles(rootDirectory, $"*{MAP_EXTENSION}");
                }
            }
            catch (System.Exception e)
            {
                logger.ErrorLog("Failed to get files: " + e.Message);
            }

            return new string[] { };
        }

        /// Returns list of all stages downloaded from synthriderz.com located in the given directory.
        /// If none found or error occurs, returns empty array
        public string[] GetSynthriderzStageFiles(string rootDirectory)
        {
            try
            {
                var directoryExists = Directory.Exists(rootDirectory);
                logger.DebugLog($"Getting stage files from {rootDirectory}. Directory exists? {directoryExists}");
                if (directoryExists)
                {
                    var filePaths = new List<string>();
                    foreach (var stageExtension in STAGE_EXTENSIONS)
                    {
                        filePaths.AddRange(Directory.GetFiles(rootDirectory, $"*{stageExtension}"));
                    }
                    return filePaths.ToArray();
                }
            }
            catch (System.Exception e)
            {
                logger.ErrorLog("Failed to get files: " + e.Message);
            }

            return new string[] { };
        }

        /// Returns list of all playlists downloaded from synthriderz.com located in the given directory.
        /// If none found or error occurs, returns empty array
        public string[] GetSynthriderzPlaylistFiles(string rootDirectory)
        {
            try
            {
                var directoryExists = Directory.Exists(rootDirectory);
                logger.DebugLog($"Getting playlist files from {rootDirectory}. Directory exists? {directoryExists}");
                if (directoryExists)
                {
                    var filePaths = new List<string>();
                    return Directory.GetFiles(rootDirectory, $"*{PLAYLIST_EXTENSION}");
                }
            }
            catch (System.Exception e)
            {
                logger.ErrorLog("Failed to get files: " + e.Message);
            }

            return new string[] { };
        }

        /// Returns a new list with all maps in the source list that aren't contained in the user's custom song directory already
        public List<MapItem> FilterOutExistingMaps(List<MapItem> maps)
        {
            logger.DebugLog($"{db.GetNumberOfMaps()} local maps found");
            return maps.Where(mapItem => !string.IsNullOrEmpty(mapItem.hash) && db.GetFromHash(mapItem.hash) == null).ToList();
        }

        /// Refreshes local database metadata. Parses all missing custom map files.
        /// This saves the updated database.
        public async Task RefreshLocalDatabase()
        {
            var localHashes = new HashSet<string>();

            // Make sure local db state is up to date
            await db.Load();

            try
            {
                var mapsDir = FileUtils.CustomSongsPath;
                if (!Directory.Exists(mapsDir))
                {
                    logger.ErrorLog($"Custom maps directory doesn't exist at {mapsDir}! Creating...");
                    Directory.CreateDirectory(mapsDir);
                }

                var files = Directory.GetFiles(mapsDir, $"*{MAP_EXTENSION}");
                logger.DebugLog($"Updating database with map files ({files.Length} found)...");
                // This will implicitly remove any entries that are only present in the db
                int count = 0;
                int totalFiles = files.Length;
                foreach (var filePath in files)
                {
                    var dbMetadata = db.GetFromPath(filePath);
                    if (dbMetadata != null)
                    {
                        // DB has this version already - good to go
                        // logger.DebugLog(Path.GetFileName(filePath) + " already in db");
                        localHashes.Add(dbMetadata.hash);
                    }
                    else
                    {
                        // DB doesn't have this version; parse and add
                        var metadata = await ParseLocalMap(filePath, logger);
                        if (metadata == null)
                        {
                            logger.ErrorLog("Failed to parse map " + Path.GetFileNameWithoutExtension(filePath));
                            continue;
                        }

                        localHashes.Add(metadata.hash);
                        db.AddMap(metadata, logger);
                    }

                    count++;
                    
                    // Don't hog main thread
                    if (count % 20 == 0)
                    {
                        await Task.Yield();
                    }
                    
                    if (count % 1000 == 0)
                    {
                        logger.DebugLog($"Processed {count}/{totalFiles}...");

                        // Save partial progress; ignore errors
                        await db.Save();
                    }
                }
                logger.DebugLog($"{totalFiles} local files processed");
            }
            catch (System.Exception e)
            {
                logger.ErrorLog($"Failed to get local maps: {e.Message}");
                return;
            }

            // Successfully loaded maps
            // Remove all db entries that are no longer on the local file system
            logger.DebugLog("Removing database entries that aren't on the local file system...");
            db.RemoveMissingHashes(localHashes);

            // Save to db for next run
            if (!await db.Save())
            {
                logger.ErrorLog("Failed to save db");
                // ignore for now; we still loaded everything fine
            }
        }

        /// Parses local map file. Returns null if can't parse or no metadata
        public static async Task<MapZMetadata?> ParseLocalMap(string filePath, SRLogHandler logger, MapItem? mapFromZ = null)
        {
            var metadataFileName = "synthriderz.meta.json";
            try
            {
                using (Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
                using (BufferedStream bufferedStream = new BufferedStream(stream))
                using (ZipArchive archive = new ZipArchive(bufferedStream, ZipArchiveMode.Update))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName == metadataFileName)
                        {
                            using (System.IO.StreamReader sr = new System.IO.StreamReader(entry.Open()))
                            {
                                MapZMetadata? metadata =
                                    JsonConvert.DeserializeObject<MapZMetadata>(await sr.ReadToEndAsync());
                                if (metadata != null)
                                {
                                    metadata.FilePath = filePath;
                                    return metadata;
                                }
                            }
                        }
                    }

                    // No return, so missing metadata file.
                    var fileName = Path.GetFileName(filePath);
                    logger.DebugLog($"Missing {metadataFileName} in map {fileName}");
                    if (mapFromZ == null || mapFromZ.hash == null || mapFromZ.id <= 0)
                    {
                        logger.ErrorLog($"Missing {metadataFileName}!");
                    }
                    else
                    {
                        // We have information from Z to add this in ourselves
                        logger.ErrorLog($"Creating missing {metadataFileName} for {fileName}");
                        try
                        {
                            JObject zMetadata = new JObject(
                                new JProperty("id", mapFromZ.id),
                                new JProperty("hash", mapFromZ.hash)
                            );

                            var newEntry = archive.CreateEntry(metadataFileName);
                            using StreamWriter streamWriter = new StreamWriter(newEntry.Open());
                            await streamWriter.WriteAsync(zMetadata.ToString(Formatting.None));

                            return new MapZMetadata(mapFromZ.id, mapFromZ.hash, filePath);
                        }
                        catch (Exception e)
                        {
                            logger.ErrorLog("Failed to create missing metadata entry: " + e.Message);
                        }
                    }
                }
            }
            catch (System.IO.InvalidDataException)
            {
                // The file is somehow corrupted; delete to allow retry
                logger.ErrorLog($"Invalid local map file {Path.GetFileNameWithoutExtension(filePath)}; deleting");
                FileUtils.DeleteFile(filePath, logger);
            }
            catch (System.Exception e)
            {
                logger.ErrorLog($"Failed to parse local map {Path.GetFileNameWithoutExtension(filePath)}: {e.Message}");
            }

            return null;
        }

        /// <summary>
        /// Loads the local hash => timestamp mappings from local file.
        /// Returns a non-null mapping w/ no pairs on failure.
        /// </summary>
        /// <returns></returns>
        public async Task<LocalMapTimestampMappings> GetLocalTimestampMappings()
        {
            var mappings = new LocalMapTimestampMappings();

#if UNITY_2021_3_OR_NEWER
            var localTimestampMapping = await Unity.Template.VR.AddressableUtil.LoadAndParseText<List<MapItem>>("sr_timestamp_mapping");
#else
            var localTimestampMapFile = FileUtils.MappingFilePath;
            if (!File.Exists(localTimestampMapFile))
            {
                logger.ErrorLog("No mapping file found!");
                return mappings;
            }

            var localTimestampMapping = JsonConvert.DeserializeObject<List<MapItem>>(File.ReadAllText(localTimestampMapFile));
            await Task.CompletedTask;
#endif
            
            if (localTimestampMapping == null || localTimestampMapping.Count == 0)
            {
                logger.ErrorLog("Failed to read mappings from file!");
                return mappings;
            }

            foreach (var mapping in localTimestampMapping)
            {
                //logger.DebugLog($"Mapping {mapping.hash} {mapping.modified_time} {mapping.modified_time_formatted}");
                mappings.Add(mapping);
            }

            return mappings;
        }

        /// <summary>
        /// Applies the given timestamp corrections to all found local files.
        /// </summary>
        /// <param name="mappings"></param>
        public async Task ApplyLocalMappings(LocalMapTimestampMappings mappings)
        {
            var numProcessed = 0;
            foreach (var mapping in mappings.MapTimestamps)
            {
                if (string.IsNullOrEmpty(mapping.hash))
                    continue;
                
                numProcessed++;
                
                // If we have a local file for this mapping, apply the fix
                var localMap = db.GetFromHash(mapping.hash);
                if (localMap != null)
                {
                    if (!mappings.TryGetDateModified(localMap.hash, out var dateModifiedUtc))
                    {
                        logger.ErrorLog($"Couldn't get date modified for {localMap.hash}");
                        continue;
                    }

                    if (FileUtils.TrySetDateModifiedUtc(localMap.FilePath, dateModifiedUtc, logger))
                    {
                        // logger.DebugLog($"Updated date modified for {Path.GetFileNameWithoutExtension(localMap.FilePath)} to {dateModifiedUtc}");
                    }
                    else
                    {
                        logger.ErrorLog($"Failed to update date modified for {localMap.FilePath} to {dateModifiedUtc.ToString()} ({localMap.hash})");
                    }
                }
                else
                {
                    //logger.DebugLog("No local map found for mapping " + mapping.hash);
                }
                
                // Don't hog the main thread
                if (numProcessed % 20 == 0)
                {
                    await Task.Yield();
                }
                
                // Let user know work is being done
                if (numProcessed % 500 == 0)
                {
                    logger.DebugLog($"Processed {numProcessed} / {mappings.MapTimestamps.Count}...");
                }
            }
            
            logger.DebugLog($"Processed {mappings.MapTimestamps.Count} / {mappings.MapTimestamps.Count}...");
            logger.DebugLog("Finished applying local timestamp mappings");
        }

        /// <summary>
        /// Use the local song info to generate a fake, but ordered, timestamp based on the song's id.
        /// Ending timestamp is Jan 2, 2019 (before any maps were uploaded) + id minutes
        /// </summary>
        public void ApplyTimestampsFromIds()
        {
            var localMaps = db.GetLocalMapsCopy();
            
            // Use Jan 2, 2019 so that time offsets at most go to Jan 1 and not a diff year,
            // and so Windows actually shows the timestamp since it's sane/recent
            var startTime = new DateTime(2019, 1, 2);
            
            foreach (var map in localMaps)
            {
                var fileName = Path.GetFileNameWithoutExtension(map.FilePath);

                // Skip drafts
                if (fileName.StartsWith("DRAFT"))
                    continue;
                
                var fakeTimestamp = startTime + TimeSpan.FromMinutes(map.id);
                logger.DebugLog($"Song {fileName}, id {map.id}, fake time {fakeTimestamp}");
                FileUtils.TrySetDateModifiedUtc(map.FilePath, fakeTimestamp, logger);
            }
        }

        /// <summary>
        /// Since the Z site is currently down, use this method to generate a local file that can be used initially to fix map timestamps.
        /// </summary>
        public void RefreshLocalMapTimestampMapping()
        {
            var mapTimestamps = GetLocalMapTimestamps();
            if (mapTimestamps.Count == 0)
            {
                logger.ErrorLog("No map timestamps found!");
                return;
            }

            var mappingFilePath = FileUtils.MappingFilePath;
            try
            {
                File.WriteAllText(mappingFilePath, JsonConvert.SerializeObject(mapTimestamps, Formatting.Indented));
                logger.DebugLog($"Updated mapping file at {mappingFilePath}");
            }
            catch (Exception e)
            {
                logger.ErrorLog("Failed to write timestamp mapping: " + e.Message);
            }
        }
        
        /// <summary>
        /// Updates the SynthDB file with timestamp info for each of the given maps
        /// </summary>
        public async Task UpdateSynthDBTimestamps()
        {
            logger.DebugLog("Updating SynthDB timestamps...");
            var synthDbPath = FileUtils.SynthDBPath;
            var localMaps = db.GetLocalMapsCopy();

            SQLiteConnection conn;
            SQLiteCommand cmdUpdateTime;
            try
            {
                conn = new SQLiteConnection($"{synthDbPath}", SQLiteOpenFlags.ReadWrite);
                cmdUpdateTime = conn.CreateCommand(
                        @"UPDATE TracksCache SET date_created = @date_created WHERE leaderboard_hash = @leaderboard_hash");
            }
            catch (Exception e)
            {
                logger.ErrorLog(e.Message);
                return;
            }

            var numProcessed = 0;
            foreach (var map in localMaps)
            {
                var lastWriteTimeUtc = File.GetLastWriteTimeUtc(map.FilePath);
                int secSinceEpoch = (int)(lastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds;

                try
                {
                    cmdUpdateTime.Bind("@date_created", secSinceEpoch);
                    cmdUpdateTime.Bind("@leaderboard_hash", map.hash);

                    cmdUpdateTime.ExecuteNonQuery();
                    numProcessed++;
                }
                catch (Exception e)
                {
                    logger.ErrorLog(e.Message);
                }

                // Don't hog the main thread
                if (numProcessed % 20 == 0)
                {
                    await Task.Yield();
                }
                
                // Let the user know work is being done
                if (numProcessed % 500 == 0)
                {
                    logger.DebugLog($"  {numProcessed} / {localMaps.Count} processed");
                }
            }
            
            logger.DebugLog($"  {localMaps.Count} / {localMaps.Count} processed");
            logger.DebugLog("Finished updating SynthDB");
        }

        private List<MapTimestamp> GetLocalMapTimestamps()
        {
            logger.DebugLog("Getting map timestamps...");

            var mapTimestamps = new List<MapTimestamp>();

            try
            {
                var mapPaths = GetCustomMapPaths();
                foreach (var mapPath in mapPaths)
                {
                    var mapMetadata = db.GetFromPath(mapPath);
                    if (mapMetadata == null)
                    {
                        logger.DebugLog($"No local map metadata for {mapPath}; skipping");
                        continue;
                    }

                    if (FileUtils.TryGetDateModifiedUtc(mapPath, logger, out var dateModifiedUtc))
                    {
                        var mapHash = mapMetadata.hash;
                        mapTimestamps.Add(new MapTimestamp
                        {
                            hash = mapHash,
                            modified_time = (long)((dateModifiedUtc - DateTime.UnixEpoch).TotalSeconds),
                            modified_time_formatted = dateModifiedUtc.ToString(),
                        });
                    }
                }
            }
            catch (Exception e)
            {
                logger.ErrorLog("Getting map timestamps failed: " + e.Message);
                return new List<MapTimestamp>();
            }

            return mapTimestamps;
        }
    }
}