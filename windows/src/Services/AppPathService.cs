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

        /// <summary>Histórico protegido pelo Windows (DPAPI). O history.json em texto puro é das versões antigas.</summary>
        public static string ProtectedHistoryFilePath
        {
            get { return Path.Combine(_dataDirectory, "history.dat"); }
        }

        public static string DictionaryFilePath
        {
            get { return Path.Combine(_dataDirectory, "dictionary.json"); }
        }

        public static string CredentialsFilePath
        {
            get { return Path.Combine(_dataDirectory, "credentials.dat"); }
        }

        /// <summary>Chave da Groq protegida pelo Windows (DPAPI).</summary>
        public static string ProtectedApiKeyFilePath
        {
            get { return Path.Combine(_dataDirectory, "api_key.dpapi"); }
        }

        /// <summary>Chave em texto puro das versões antigas; só é lida para migrar e depois apagada.</summary>
        public static string ApiKeyFilePath
        {
            get { return Path.Combine(_dataDirectory, "api_key.txt"); }
        }

        public static void InitializePaths()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string portableDataDir = Path.Combine(appDir, "data");
            string nonPortableMarkerFile = Path.Combine(appDir, "non_portable.txt");

            // Portable Mode is now the DEFAULT behavior for VoxIA
            if (File.Exists(nonPortableMarkerFile))
            {
                _isPortableMode = false;
                _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxIA");
            }
            else
            {
                _isPortableMode = true;
                _dataDirectory = portableDataDir;

                string portableMarkerFile = Path.Combine(appDir, "portable.txt");
                try
                {
                    if (!File.Exists(portableMarkerFile))
                    {
                        File.WriteAllText(portableMarkerFile, "VoxIA Portable Mode Marker");
                    }
                }
                catch { }
            }

            EnsureDirectoriesExist();
        }

        public static void EnablePortableMode(bool enable)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string portableMarkerFile = Path.Combine(appDir, "portable.txt");
            string nonPortableMarkerFile = Path.Combine(appDir, "non_portable.txt");
            string portableDataDir = Path.Combine(appDir, "data");

            if (enable)
            {
                if (File.Exists(nonPortableMarkerFile))
                {
                    try { File.Delete(nonPortableMarkerFile); } catch { }
                }

                if (!Directory.Exists(portableDataDir))
                    Directory.CreateDirectory(portableDataDir);

                if (!File.Exists(portableMarkerFile))
                {
                    try { File.WriteAllText(portableMarkerFile, "VoxIA Portable Mode Marker"); } catch { }
                }

                _isPortableMode = true;
                _dataDirectory = portableDataDir;
            }
            else
            {
                if (File.Exists(portableMarkerFile))
                {
                    try { File.Delete(portableMarkerFile); } catch { }
                }

                try
                {
                    File.WriteAllText(nonPortableMarkerFile, "VoxIA Non-Portable Mode Marker");
                }
                catch { }

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
