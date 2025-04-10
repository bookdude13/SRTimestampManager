using SRTimestampLib;

#if UNITY_2021_3_OR_NEWER
using UnityEngine.Networking;
#endif

namespace SRCustomLib
{
    public class HttpUtils
    {
        public static HttpUtils Instance { get; } = new();

        private HttpClient _httpClient = new();

        /// <summary>
        /// Does an HTTP GET and returns the endpoint's response as a raw byte array
        /// </summary>
        /// <param name="requestUri"></param>
        /// <param name="timeoutSec"></param>
        /// <param name="logger"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<byte[]?> GetBytesAsync(Uri requestUri, int timeoutSec, SRLogHandler logger, CancellationToken cancellationToken = default)
        {
#if UNITY_2021_3_OR_NEWER
            try {
                var getRequest = UnityWebRequest.Get(requestUri);
                getRequest.timeout = timeoutSec;
                var asyncOp = getRequest.SendWebRequest();
                var startTime = DateTime.Now;
                var timeoutTime = startTime.AddSeconds(timeoutSec);
                while (!asyncOp.isDone) {
                    if (DateTime.Now > timeoutTime) {
                        logger.ErrorLog("Timed out waiting for page!");
                        return null;
                    }
                    else {
                        await Task.Delay(10);
                    }
                }
                if (!string.IsNullOrEmpty(asyncOp.webRequest.error)) {
                    logger.ErrorLog("Error getting request: " + asyncOp.webRequest.error);
                    return null;
                }
                return getRequest.downloadHandler.data;
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to get web page: {e.Message}");
                return null;
            }
#else
            try
            {
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                cancellationToken.Register(timeoutCts.Cancel);
                byte[] result = await _httpClient.GetByteArrayAsync(requestUri, timeoutCts.Token);
                
                // Apparently never returns as null, and callers might want an empty array.
                
                return result;
            }
            catch (HttpRequestException e)
            {
                logger.ErrorLog("Error getting request: " + e.Message);
                return null;
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to get web page: {e.Message}");
                return null;
            }
#endif
        }
        
        /// <summary>
        /// Does an HTTP GET and returns the endpoint's response as a string
        /// </summary>
        /// <param name="requestUri"></param>
        /// <param name="timeoutSec"></param>
        /// <param name="logger"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<string?> GetStringAsync(Uri requestUri, int timeoutSec, SRLogHandler logger, CancellationToken cancellationToken = default)
        {
#if UNITY_2021_3_OR_NEWER
            try {
                var getRequest = UnityWebRequest.Get(requestUri);
                getRequest.timeout = timeoutSec;
                var asyncOp = getRequest.SendWebRequest();
                var startTime = DateTime.Now;
                var timeoutTime = startTime.AddSeconds(timeoutSec);
                while (!asyncOp.isDone) {
                    if (DateTime.Now > timeoutTime) {
                        logger.ErrorLog("Timed out waiting for page!");
                        return null;
                    }
                    else {
                        await Task.Delay(10);
                    }
                }
                if (!string.IsNullOrEmpty(asyncOp.webRequest.error)) {
                    logger.ErrorLog("Error getting request: " + asyncOp.webRequest.error);
                    return null;
                }
                return getRequest.downloadHandler.text;
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to get web page: {e.Message}");
                return null;
            }
#else
            try
            {
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                cancellationToken.Register(timeoutCts.Cancel);
                string? result = await _httpClient.GetStringAsync(requestUri, timeoutCts.Token);
                if (string.IsNullOrEmpty(result))
                {
                    logger.ErrorLog("No result for request!");
                    return null;
                }

                return result;
            }
            catch (HttpRequestException e)
            {
                logger.ErrorLog("Error getting request: " + e.Message);
                return null;
            }
            catch (System.Exception e) {
                logger.ErrorLog($"Failed to get web page: {e.Message}");
                return null;
            }
#endif
        }
    }
}