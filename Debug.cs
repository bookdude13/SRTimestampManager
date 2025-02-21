using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SRTimestampFileGenerator
{
    /// <summary>
    /// Allows for easier mapping between Unity code and this utility
    /// </summary>
    public class Debug
    {
        public static void Log(string message) => Console.WriteLine(message);
        public static void LogError(string message) => Console.Error.WriteLine(message);
    }
}
