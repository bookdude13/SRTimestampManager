using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

// Avoid annoying warnings in Unity
#nullable enable

namespace SRTimestampLib
{
    public static class FileUtils
    {
        // Hardcoded expected path for Android builds, but let the editor (and other console apps) try to figure it out
#if UNITY_ANDROID && !UNITY_EDITOR
        public readonly static string SynthCustomContentDir = "/sdcard/SynthRidersUC/";
#else
        private static string GetDefaultSynthCustomContentDir()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return @"C:\Program Files (x86)\Steam\steamapps\common\SynthRiders\SynthRidersUC\";
            }

            if (Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                var relativePath = "~/Library/Application Support/Steam/steamapps/common/SynthRiders/SynthRidersUC/";
                return Path.GetFullPath(relativePath);
            }

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                var relativePath = "~/.steam/steam/steamapps/common/SynthRiders/SynthRidersUC/";
                return Path.GetFullPath(relativePath);
            }

            // Debug.LogError("Unknown platform " + Environment.OSVersion.Platform);
            return "/sdcard/SynthRidersUC";
        }

        public static string OverrideSynthCustomContentDir = "";
        public static string SynthCustomContentDir => !string.IsNullOrEmpty(OverrideSynthCustomContentDir) ? OverrideSynthCustomContentDir : GetDefaultSynthCustomContentDir();
#endif

        public static string MappingFilePath
        {
            get { return Path.Combine(".", "sr_timestamp_mapping.json"); }
        }
        
        public static string TorrentCacheDirectory
        {
            // Take advantage of the custom dir being present, and add our own
            get
            {
                var cacheDir = Path.Combine(SynthCustomContentDir, "SongDlCache");
                Directory.CreateDirectory(cacheDir);
                return cacheDir;
            }
        }

        public static string CachedTorrentFile => Path.Combine(TorrentCacheDirectory, "cached.torrent");
        
        public static string TorrentDownloadDirectory
        {
            // Take advantage of the custom dir being present, and add our own
            get
            {
                var cacheDir = Path.Combine(SynthCustomContentDir, "SongDl");
                Directory.CreateDirectory(cacheDir);
                return cacheDir;
            }
        }

        public static string CustomSongsPath => Path.Join(SynthCustomContentDir, "CustomSongs");
        public static string SynthDBPath => Path.GetFullPath(Path.Join(SynthCustomContentDir, "SynthDB"));

        public static string TempPath
        {
#if UNITY_2021_3_OR_NEWER
            get { return UnityEngine.Application.temporaryCachePath; }
#else
            get { return Path.GetTempPath(); }
#endif
        }

        public static string GetPersistentFolder()
        {
#if UNITY_2021_3_OR_NEWER
            return UnityEngine.Application.persistentDataPath;
#else
            var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var persistentDir = Path.Combine(localAppData, "SRTimestampFileGenerator");

            // Ensure it exists!
            Directory.CreateDirectory(persistentDir);

            return persistentDir;
#endif
        }

        /// Attempts to move a file, overwriting if dstPath already exists.
        /// Returns true if it succeeded, false if it failed.
        public static bool MoveFileOverwrite(string srcPath, string destPath, SRLogHandler logger)
        {
            try
            {
                File.Copy(srcPath, destPath, true);
                File.Delete(srcPath);
                return true;
            }
            catch (System.Exception e)
            {
                logger.ErrorLog($"Failed to move {srcPath} to {destPath}! {e.Message}");
            }

            return false;
        }

        public static async Task<bool> WriteToFile(byte[] bytes, string filePath, SRLogHandler logger)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite))
                {
                    await fs.WriteAsync(bytes, 0, bytes.Length);
                    return true;
                }
            }
            catch (System.Exception e)
            {
                logger.ErrorLog($"Failed to write to {filePath}: {e.Message}");
                return false;
            }
        }

        public static async Task<bool> WriteToFile(string contents, string filePath, SRLogHandler logger)
        {
            return await WriteToFile(Encoding.UTF8.GetBytes(contents), filePath, logger);
        }

        public static async Task<bool> AppendToFile(byte[] bytes, string filePath, SRLogHandler logger)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Append, FileAccess.Write))
                {
                    await fs.WriteAsync(bytes, 0, bytes.Length);
                    return true;
                }
            }
            catch (System.Exception e)
            {
                logger.ErrorLog("Failed to append to file: " + e.Message);
                return false;
            }
        }

        public static async Task<bool> AppendToFile(string contents, string filePath, SRLogHandler logger)
        {
            return await AppendToFile(Encoding.UTF8.GetBytes(contents), filePath, logger);
        }

        /// Reads file contents and parses into given type. Assumes json input.
        /// Returns null on failure.
        public static async Task<T?> ReadFileJson<T>(string filePath, SRLogHandler logger)
        {
            try
            {
                using (Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BufferedStream bufferedStream = new BufferedStream(stream))
                using (System.IO.StreamReader sr = new System.IO.StreamReader(bufferedStream))
                {
                    var metadata = JsonConvert.DeserializeObject<T>(await sr.ReadToEndAsync());
                    return metadata;
                }
            }
            catch (System.Exception e)
            {
                logger.ErrorLog($"Failed to parse local map {Path.GetFileNameWithoutExtension(filePath)}: {e.Message}");
            }

            return default(T);
        }

        public static void EmptyDirectory(string dirPath)
        {
            var di = new DirectoryInfo(dirPath);
            foreach (var file in di.EnumerateFiles())
            {
                file.Delete(); 
            }
            foreach (var dir in di.EnumerateDirectories())
            {
                dir.Delete(true); 
            }
        }

        /// Sets file times to the given dateModified time, assuming UTC time.
        /// Return true if updated, false if error
        public static bool TrySetDateModifiedUtc(string filePath, DateTime dateModifiedUtc, SRLogHandler logger)
        {
            try
            {
                // Might as well set all of them
                File.SetLastWriteTimeUtc(filePath, dateModifiedUtc);
                File.SetLastAccessTimeUtc(filePath, dateModifiedUtc);
                File.SetCreationTimeUtc(filePath, dateModifiedUtc);

                return true;
            }
            catch (Exception e)
            {
                logger.ErrorLog($"Failed to set file dates for {Path.GetFileName(filePath)}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to get the date modified of the given file
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <param name="dateModifiedUtc">The date modified if this returns true, else DateTime.UnixEpoch</param>
        /// <param name="logger"></param>
        /// <returns>True if date was retrieved, false on failure (i.e. file not found)</returns>
        public static bool TryGetDateModifiedUtc(string filePath, SRLogHandler logger, out DateTime dateModifiedUtc)
        {
            dateModifiedUtc = DateTime.UnixEpoch;

            try
            {
                dateModifiedUtc = File.GetLastWriteTimeUtc(filePath);
                return true;
            }
            catch (Exception e)
            {
                logger.ErrorLog($"Failed to get file dates for {Path.GetFileName(filePath)}: {e.Message}");
                return false;
            }
        }

        public static bool DeleteFile(string filePath, SRLogHandler logger)
        {
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (Exception e)
            {
                logger.ErrorLog($"Failed to delete file at path {filePath}: {e.Message}");
                return false;
            }
        }
    }
}