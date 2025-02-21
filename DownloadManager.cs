using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SRTimestampFileGenerator
{
    public class DownloadManager
    {
        private SRLogHandler logger;

        public DownloadManager(SRLogHandler logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Since the Z site is currently down, use this method to generate a local file that can be used initially to fix map timestamps.
        /// </summary>
        public void RefreshLocalMapTimestampMapping()
        {
            var mapTimestamps = GetLocalMapTimestamps();
            if (mapTimestamps.Count == 0)
            {
                Debug.LogError("No map timestamps found!");
                return;
            }

            var mappingFilePath = Path.Join(Application.persistentDataPath, "timestampMapping.json");
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

        private List<MapTimestampPair> GetLocalMapTimestamps()
        {
            logger.DebugLog("Getting map timestamps...");

            var mapTimestamps = new List<MapTimestampPair>();

            try
            {
                var mapPaths = customFileManager.GetCustomSongPaths();
                foreach (var mapPath in mapPaths)
                {
                    var mapMetadata = customFileManager.db.GetFromPath(mapPath);
                    if (mapMetadata == null)
                    {
                        logger.DebugLog($"No local map metadata for {mapPath}; skipping");
                        continue;
                    }

                    if (FileUtils.TryGetDateModifiedUtc(mapPath, logger, out var dateModifiedUtc))
                    {
                        var mapHash = mapMetadata.hash;
                        mapTimestamps.Add(new MapTimestampPair
                        {
                            MapHash = mapHash,
                            DateModifiedUtc = dateModifiedUtc,
                        });
                    }
                }
            }
            catch (Exception e)
            {
                logger.ErrorLog("Getting map timestamps failed: " + e.Message);
                return new List<MapTimestampPair>();
            }

            return mapTimestamps;
        }

        /// Update local map timestamps to match the Z site published_at,
        /// to allow for correct sorting by timestamp in-game
        public async void FixMapTimestamps()
        {
            RefreshLocalMapTimestampMapping();
            return;

            logger.DebugLog("Fixing map timestamp...");

            displayManager.DisableActions("Fixing Timestamps...");

            try
            {
                var sinceTime = DateTime.UnixEpoch;
                var selectedDifficulties = downloadFilters.GetAllDifficulties();

                logger.DebugLog("Getting all maps from Z");
                List<MapItem> mapsFromZ = await GetMapsSinceTimeForDifficulties(sinceTime, selectedDifficulties);

                logger.DebugLog("Fixing local files...");
                var notFoundLocally = 0;
                foreach (var mapFromZ in mapsFromZ)
                {
                    MapZMetadata localMetadata = customFileManager.db.GetFromHash(mapFromZ.hash);
                    if (localMetadata == null)
                    {
                        notFoundLocally++;
                        // logger.DebugLog($"Map id {mapFromZ.id} not found locally, skipping");
                    }
                    else
                    {
                        var fileName = Path.GetFileName(localMetadata.FilePath);
                        var publishedAtUtc = mapFromZ.GetPublishedAtUtc();
                        if (publishedAtUtc == null)
                        {
                            logger.DebugLog($"Couldn't parse published_at timestamp {mapFromZ.published_at}");
                        }
                        else
                        {
                            logger.DebugLog($"Setting timestamp for {fileName} to {publishedAtUtc}");
                            FileUtils.SetDateModifiedUtc(localMetadata.FilePath, publishedAtUtc.GetValueOrDefault(), logger);
                        }
                    }
                }

                logger.DebugLog($"{notFoundLocally} files from Z not found locally and skipped");
                logger.DebugLog("Finished correcting timestamps");
            }
            catch (Exception e)
            {
                logger.ErrorLog("Failed to fix timestamps: " + e.Message);
            }

            displayManager.EnableActions();
        }
    }
}