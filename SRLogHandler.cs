using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SRTimestampFileGenerator
{
    /// <summary>
    /// Abstracts logging. Matches with SRQuestDownloader
    /// </summary>
    public class SRLogHandler
    {
        public void DebugLog(string message) => Debug.Log(message);
        public void ErrorLog(string message) => Debug.LogError(message);
    }
}
