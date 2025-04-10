using Newtonsoft.Json;
using SRTimestampLib;
using SRTimestampLib.Models;

namespace SRCustomLib
{
    /// <summary>
    /// Handles custom map downloading via Synplicity API.
    /// It's mostly compatible with the Z format, so subclassing instead of making a whole new class.
    /// </summary>
    public class DownloadManagerSyn : DownloadManagerZ
    {
        public DownloadManagerSyn(SRLogHandler logger, CustomFileManager customFileManager) : base(logger, customFileManager) { }

        public override string GetDownloadUrl(MapItem map)
        {
            return "https://api.synplicity.live/" + map.download_url;
        }

        public override string MapPageUrl => "https://api.synplicity.live/beatmaps";

        /// <summary>
        /// Gets a single page of map metadata, with the given filters
        /// </summary>
        /// <param name="pageSize"></param>
        /// <param name="pageIndex"></param>
        /// <param name="sinceTime"></param>
        /// <param name="includedDifficulties"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task<MapPage?> GetMapPage(int pageSize, int pageIndex, DateTimeOffset sinceTime, List<string>? includedDifficulties, CancellationToken cancellationToken = default)
        {
            var apiEndpoint = MapPageUrl;
            var sort = "published_at,DESC";

            string sinceTimeIso8601 = sinceTime.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
            // string difficultiesArray = string.Join(",", includedDifficulties.Select(diff => diff.ToLowerInvariant()));
            // string difficultiesParam = includedDifficulties.Count > 0 ? $"&difficulties={difficultiesArray}" : "";
            string difficultiesParam = "";
            if (includedDifficulties != null && includedDifficulties.Count > 0)
            {
                difficultiesParam = "&difficulties=" + string.Join(",", includedDifficulties.Select(diff => diff.ToLowerInvariant()));
                // foreach (var diff in includedDifficulties)
                // {
                //     difficultiesParam += $"&difficulties={diff.ToLowerInvariant()}";
                // }
            }

            string request = $"{apiEndpoint}?sort={sort}&limit={pageSize}&page={pageIndex}&date_after={sinceTimeIso8601}{difficultiesParam}";
            var requestUri = new Uri(request);
            string? rawPage = await HttpUtils.Instance.GetStringAsync(requestUri, GET_PAGE_TIMEOUT_SEC, logger, cancellationToken);
            if (string.IsNullOrEmpty(rawPage))
            {
                logger.ErrorLog($"Failed to get map page {pageIndex}, url {requestUri}");
                return null;
            }

            logger.DebugLog("Deserializing page...");
            try
            {
                MapPage? page = JsonConvert.DeserializeObject<MapPage>(rawPage);
                return page;
            }
            catch (System.Exception e)
            {
                logger.ErrorLog($"Failed to deserialize map page: {e.Message}");
                return null;
            }
        }
    }
}