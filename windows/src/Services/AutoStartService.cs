using System;
using Microsoft.Win32;

namespace VoxIA.Services
{
    public static class AutoStartService
    {
        private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "VoxIA";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(AppName);
                        return val != null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error checking AutoStart registry key", ex);
            }
            return false;
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = System.Windows.Forms.Application.ExecutablePath;
                            key.SetValue(AppName, string.Format("\"{0}\"", exePath));
                            Logger.LogInfo("AutoStart registry key set.");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                            Logger.LogInfo("AutoStart registry key removed.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error toggling AutoStart registry key", ex);
            }
        }
    }
}
