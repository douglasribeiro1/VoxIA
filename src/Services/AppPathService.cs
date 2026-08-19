using System;
using System.IO;

namespace VoxIA.Services
{
    public static class AppPathService
    {
        private static string _dataDirectory;
        private static bool _isPortableMode;

        static AppPathService()
        {
            InitializePaths();
        }

        public static bool IsPortableMode
        {
            get { return _isPortableMode; }
        }

        public static string DataDirectory
        {
            get { return _dataDirectory; }
        }

        public static string AudioQueueDirectory
        {
            get { return Path.Combine(_dataDirectory, "audio_queue"); }
        }

        public static string LogsDirectory
        {
            get { return Path.Combine(_dataDirectory, "logs"); }
        }

        public static string SettingsFilePath
        {
            get { return Path.Combine(_dataDirectory, "settings.json"); }
        }

        public static string QueueFilePath
        {
            get { return Path.Combine(_dataDirectory, "queue.json"); }
        }

        public static string HistoryFilePath
        {
            get { return Path.Combine(_dataDirectory, "history.json"); }
        }

        public static string DictionaryFilePath
        {
            get { return Path.Combine(_dataDirectory, "dictionary.json"); }
        }

        public static string CredentialsFilePath
        {
            get { return Path.Combine(_dataDirectory, "credentials.dat"); }
        }

        public static void InitializePaths()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string portableDataDir = Path.Combine(appDir, "data");
            string portableMarkerFile = Path.Combine(appDir, "portable.txt");

            if (Directory.Exists(portableDataDir) || File.Exists(portableMarkerFile))
            {
                _isPortableMode = true;
                _dataDirectory = portableDataDir;
            }
            else
            {
                _isPortableMode = false;
                _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxIA");
            }

            EnsureDirectoriesExist();
        }

        public static void EnablePortableMode(bool enable)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string portableMarkerFile = Path.Combine(appDir, "portable.txt");
            string portableDataDir = Path.Combine(appDir, "data");

            if (enable)
            {
                if (!Directory.Exists(portableDataDir))
                    Directory.CreateDirectory(portableDataDir);

                if (!File.Exists(portableMarkerFile))
                    File.WriteAllText(portableMarkerFile, "VoxIA Portable Mode Marker");

                _isPortableMode = true;
                _dataDirectory = portableDataDir;
            }
            else
            {
                if (File.Exists(portableMarkerFile))
                {
                    try { File.Delete(portableMarkerFile); } catch { }
                }
                _isPortableMode = false;
                _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxIA");
            }

            EnsureDirectoriesExist();
        }

        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(_dataDirectory))
                Directory.CreateDirectory(_dataDirectory);

            if (!Directory.Exists(AudioQueueDirectory))
                Directory.CreateDirectory(AudioQueueDirectory);

            if (!Directory.Exists(LogsDirectory))
                Directory.CreateDirectory(LogsDirectory);
        }
    }
}
