using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRTimestampLib;
using SRTimestampLib.Models;

namespace SRCustomLib
{
    /// <summary>
    /// Handles map downloads through the Z site
    /// </summary>
    public class DownloadManagerZ
    {
        public SRLogHandler logger;
        public CustomFileManager customFileManager;
        
        protected const int GET_PAGE_TIMEOUT_SEC = 3; // In case the site is down, fail quick
        protected const int GET_MAP_TIMEOUT_SEC = 60;
        protected const int PARALLEL_DOWNLOAD_LIMIT = 10;

        public DownloadManagerZ(SRLogHandler logger, CustomFileManager customFileManager)
        {
            this.logger = logger;
            this.customFileManager = customFileManager;
        }

        public virtual string GetDownloadUrl(MapItem map)
        {
            return "https://synthriderz.com" + map.download_url;
        }

        public virtual string MapPageUrl => "https://synthriderz.com/api/beatmaps";

        /// <summary>
        /// Tries to download all songs from Z with any of the given difficulties and published after the given time.
        /// </summary>
        /// <param name="sinceTime"></param>
        /// <param name="selectedDifficulties"></param>
        /// <returns>True if successfully downloaded (or attempted but none to download). False on failure (i.e. Z is down)</returns>
        public async Task<bool> DownloadSongsSinceTime(DateTimeOffset sinceTime, List<string>? selectedDifficulties) {
            logger.DebugLog($"Getting maps after time {sinceTime.ToLocalTime()}...");

            var tempDir = Path.Join(FileUtils.TempPath, "Download");
            try {
                // Delete anything from previous runs
                if (Directory.Exists(tempDir)) {
                    logger.DebugLog($"Clearing temp directory at {tempDir}");
                    Directory.Delete(tempDir, true);
                }

                // Recreate
                logger.DebugLog($"(re)Creating temp directory at {tempDir}");
                Directory.CreateDirectory(tempDir);
            }
            catch (System.Exception e) {
                logger.ErrorLog("Failed to delete or create temp dir: " + e.Message);
                return false;
            }
            
            try {
                List<MapItem>? mapsFromSite = await GetMapsSinceTimeForDifficulties(sinceTime, selectedDifficulties);
                if (mapsFromSite == null)
                {
                    return false;
                }
                
                logger.DebugLog($"{mapsFromSite.Count} maps found since given time for given difficulties.");

                var mapsToDownload = customFileManager.FilterOutExistingMaps(mapsFromSite);
                logger.DebugLog($"{mapsToDownload.Count} new files to download...");

                int count = 1;
                var downloadTasks = new Queue<Task<bool>>();
                foreach (MapItem map in mapsToDownload) {
                    logger.DebugLog($"{count}/{mapsToDownload.Count}: {map.id} {map.title}");

                    downloadTasks.Enqueue(DownloadMap(map, tempDir));
                    count++;

                    // Limit the number of simultaneous downloads
                    while (downloadTasks.Count > PARALLEL_DOWNLOAD_LIMIT) {
                        // logger.DebugLog($"More than {PARALLEL_DOWNLOAD_LIMIT} downloads queued, waiting...");
                        // This is safe since we aren't adding any more to this while we wait
                        await downloadTasks.Dequeue();
                    }

                    // Every so often for bulk downloads, save the database
                    if (count % 100 == 0) {
                        logger.DebugLog("Stopping new queued downloads...");
                        while (downloadTasks.Count > 0) {
                            await downloadTasks.Dequeue();
                        }

                        logger.DebugLog("Saving current state to db...");
                        await customFileManager.db.Save();

                        logger.DebugLog("Resuming...");
                    }
                }

                // Wait for last downloads
                logger.DebugLog("Waiting for last downloads...");
                while (downloadTasks.Count > 0) {
                    await downloadTasks.Dequeue();
                }
                logger.DebugLog("Done waiting");

                logger.DebugLog("Trying to save database...");
                await customFileManager.db.Save(true);
                logger.DebugLog("Done");
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to download maps: {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Downloads the given MapItem to the destination directory
        /// Returns true if successful, false if not
        /// </summary>
        /// <param name="map"></param>
        /// <param name="destDir"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> DownloadMap(MapItem map, string destDir, CancellationToken cancellationToken = default)
        {
            var fullUrl = GetDownloadUrl(map);
            var destPath = Path.Join(destDir, map.filename);

            try {
                byte[]? rawMapData = await HttpUtils.Instance.GetBytesAsync(new Uri(fullUrl), GET_MAP_TIMEOUT_SEC, logger, cancellationToken);
                if (rawMapData == null || rawMapData.Length == 0) {
                    logger.ErrorLog($"Error getting map data from {fullUrl}");
                    return false;
                }

                // logger.DebugLog("Saving to file...");
                if (false == await FileUtils.WriteToFile(rawMapData, destPath, logger)) {
                    return false;
                }

                // logger.DebugLog("Moving to SynthRiders directory...");
                string finalPath = customFileManager.MoveCustomSong(destPath, map.GetPublishedAtUtc());

                // logger.DebugLog("Success!");
                await customFileManager.AddLocalMap(finalPath, map);
                
                // Wait for all other operations to finish, then ensure the timestamp has been set
                await Task.Yield();
                if (map.GetPublishedAtUtc().HasValue)
                {
                    FileUtils.TrySetDateModifiedUtc(finalPath, map.GetPublishedAtUtc().GetValueOrDefault(), logger);
                }

                return true;
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to download map {map.id} ({map.title}): {e.Message}");
            }

            return false;
        }

        /// <summary>
        /// Gets one page of map metdata, with the given filters
        /// </summary>
        /// <param name="pageSize"></param>
        /// <param name="pageIndex"></param>
        /// <param name="sinceTime"></param>
        /// <param name="includedDifficulties"></param>
        /// <returns></returns>
        public virtual async Task<MapPage?> GetMapPage(int pageSize, int pageIndex, DateTimeOffset sinceTime, List<string>? includedDifficulties, CancellationToken cancellationToken = default)
        {
            var apiEndpoint = MapPageUrl;
            var sort = "published_at,DESC";

            JArray searchParameters = new JArray();

            var publishedDateRange = new JObject(
                new JProperty("published_at",
                    new JObject(
                        new JProperty("$gt",
                            new JValue(sinceTime.UtcDateTime)
                        )
                    )
                )
            );

            var beatSaberConvert = new JObject(
                new JProperty("beat_saber_convert",
                    new JObject(
                        new JProperty("$ne",
                            new JValue(true)
                        )
                    )
                )
            );

            searchParameters.Add(publishedDateRange);
            searchParameters.Add(beatSaberConvert);

            if (includedDifficulties != null)
            {
                var difficultyFilter = new JObject(
                    new JProperty("difficulties",
                        new JObject(
                            new JProperty("$jsonContainsAny",
                                new JArray(includedDifficulties)
                            )
                        )
                    )
                );
                searchParameters.Add(difficultyFilter);
            }

            var searchFilter = new JObject(
                new JProperty("$and", searchParameters)
            );

            string request = $"{apiEndpoint}?sort={sort}&limit={pageSize}&page={pageIndex}&s={searchFilter.ToString(Formatting.None)}";
            var requestUri = new Uri(request);
            string? rawPage = await HttpUtils.Instance.GetStringAsync(requestUri, GET_PAGE_TIMEOUT_SEC, logger, cancellationToken);
            if (string.IsNullOrEmpty(rawPage))
            {
                logger.ErrorLog($"Failed to get page at uri {requestUri}");
                return null;
            }

            // logger.DebugLog("Deserializing page...");
            try {
                MapPage? page = JsonConvert.DeserializeObject<MapPage>(rawPage);
                return page;
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to deserialize map page: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all maps since the given time with one of the given difficulties.
        /// Returns an empty list if none found, and null if an error occurred.
        /// </summary>
        /// <param name="sinceTime"></param>
        /// <param name="selectedDifficulties"></param>
        /// <returns></returns>
        public async Task<List<MapItem>?> GetMapsSinceTimeForDifficulties(DateTimeOffset sinceTime, List<string>? selectedDifficulties) {
            var maps = new List<MapItem>();

            int numPages = 1;
            int pageSize = 50;
            int pageIndex = 1;

            do
            {
                if (pageIndex == 1) {
                    logger.DebugLog("Requesting first page");
                }
                else {
                    logger.DebugLog($"Requesting page {pageIndex}/{numPages}");
                }

                MapPage? page = await GetMapPage(pageSize, pageIndex, sinceTime, selectedDifficulties);
                if (page == null) {
                    logger.ErrorLog($"Returned null page {pageIndex}! Aborting.");
                    return null;
                }

                logger.DebugLog($"  Page {pageIndex}/{numPages} retrieved with {page.data.Count} elements");
                foreach (MapItem item in page.data)
                {
                    // For now, don't care about existing files, just overwrite them
                    maps.Add(item);
                }

                // Set total page count from responses
                numPages = page.pagecount;

                pageIndex++;
            }
            while (pageIndex <= numPages);

            return maps;
        }
    }
}