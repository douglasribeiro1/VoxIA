using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VoxIA.Services
{
    public static class SecureCredentialService
    {
        private static readonly byte[] CryptoKey = new byte[] {
            0x56, 0x6F, 0x78, 0x49, 0x41, 0x5F, 0x53, 0x65,
            0x63, 0x75, 0x72, 0x65, 0x5F, 0x32, 0x30, 0x32,
            0x36, 0x5F, 0x47, 0x72, 0x6F, 0x71, 0x5F, 0x56,
            0x61, 0x75, 0x6C, 0x74, 0x5F, 0x4B, 0x65, 0x79
        };

        private static readonly byte[] CryptoIv = new byte[] {
            0x56, 0x6F, 0x78, 0x49, 0x41, 0x5F, 0x49, 0x56,
            0x5F, 0x32, 0x30, 0x32, 0x36, 0x5F, 0x30, 0x31
        };

        public static void SaveApiKey(string apiKey)
        {
            string cleanKey = !string.IsNullOrEmpty(apiKey) ? apiKey.Trim() : string.Empty;

            try
            {
                // Save unencrypted in SettingsService / settings.json
                SettingsService.Current.ApiKey = cleanKey;
                SettingsService.Save();

                // Also save in plain text file api_key.txt
                string plainPath = AppPathService.ApiKeyFilePath;
                if (!string.IsNullOrEmpty(cleanKey))
                {
                    File.WriteAllText(plainPath, cleanKey);
                }
                else if (File.Exists(plainPath))
                {
                    try { File.Delete(plainPath); } catch { }
                }

                // Delete legacy encrypted credentials file if present
                if (File.Exists(AppPathService.CredentialsFilePath))
                {
                    try { File.Delete(AppPathService.CredentialsFilePath); } catch { }
                }

                Logger.LogInfo("Groq API key saved in plain text (unencrypted).");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save Groq API key", ex);
            }
        }

        public static string GetApiKey()
        {
            // 1. Direct from settings.json
            if (!string.IsNullOrEmpty(SettingsService.Current.ApiKey))
                return SettingsService.Current.ApiKey.Trim();

            // 2. Direct from plain text file api_key.txt
            string plainPath = AppPathService.ApiKeyFilePath;
            if (File.Exists(plainPath))
            {
                try
                {
                    string key = File.ReadAllText(plainPath);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        SettingsService.Current.ApiKey = key.Trim();
                        return key.Trim();
                    }
                }
                catch { }
            }

            // 3. Migration: Decrypt legacy credentials.dat if it exists
            if (File.Exists(AppPathService.CredentialsFilePath))
            {
                try
                {
                    byte[] encrypted = File.ReadAllBytes(AppPathService.CredentialsFilePath);
                    byte[] raw = DecryptBytes(encrypted);
                    if (raw != null)
                    {
                        string legacyKey = Encoding.UTF8.GetString(raw);
                        if (!string.IsNullOrWhiteSpace(legacyKey))
                        {
                            SaveApiKey(legacyKey.Trim());
                            return legacyKey.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to migrate legacy credentials.dat", ex);
                }
            }

            return string.Empty;
        }

        private static byte[] EncryptBytes(byte[] plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = CryptoKey;
                aes.IV = CryptoIv;
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(plain, 0, plain.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }

        private static byte[] DecryptBytes(byte[] cipher)
        {
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = CryptoKey;
                    aes.IV = CryptoIv;
                    using (var ms = new MemoryStream())
                    {
                        using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipher, 0, cipher.Length);
                            cs.FlushFinalBlock();
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                try
                {
                    byte[] entropy = Encoding.UTF8.GetBytes("VoxIA_Secure_Entropy_2026");
                    return ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
