using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VoxIA.Services
{
    /// <summary>
    /// Guarda a chave da Groq protegida pelo Windows (DPAPI): só o mesmo usuário, no mesmo computador,
    /// consegue lê-la. Copiar a pasta do VoxIA para outro PC não leva a chave junto; lá ela é pedida de novo.
    /// </summary>
    public static class SecureCredentialService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoxIA_Secure_Entropy_2026");
        private static readonly object _lock = new object();
        private static string _cachedKey;

        // Chave e IV fixos da versão antiga do credentials.dat. Usados só para migrar e apagar esse arquivo.
        private static readonly byte[] LegacyCryptoKey = new byte[] {
            0x56, 0x6F, 0x78, 0x49, 0x41, 0x5F, 0x53, 0x65,
            0x63, 0x75, 0x72, 0x65, 0x5F, 0x32, 0x30, 0x32,
            0x36, 0x5F, 0x47, 0x72, 0x6F, 0x71, 0x5F, 0x56,
            0x61, 0x75, 0x6C, 0x74, 0x5F, 0x4B, 0x65, 0x79
        };

        private static readonly byte[] LegacyCryptoIv = new byte[] {
            0x56, 0x6F, 0x78, 0x49, 0x41, 0x5F, 0x49, 0x56,
            0x5F, 0x32, 0x30, 0x32, 0x36, 0x5F, 0x30, 0x31
        };

        public static void SaveApiKey(string apiKey)
        {
            string cleanKey = !string.IsNullOrEmpty(apiKey) ? apiKey.Trim() : string.Empty;

            lock (_lock)
            {
                try
                {
                    string path = AppPathService.ProtectedApiKeyFilePath;
                    if (cleanKey.Length > 0)
                    {
                        byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(cleanKey), Entropy, DataProtectionScope.CurrentUser);
                        File.WriteAllBytes(path, protectedBytes);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    _cachedKey = cleanKey;
                    DeleteLegacyCopies();
                    Logger.LogInfo("Groq API key saved (protected by Windows DPAPI).");
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to save Groq API key", ex);
                }
            }
        }

        public static string GetApiKey()
        {
            lock (_lock)
            {
                if (_cachedKey != null) return _cachedKey;

                string path = AppPathService.ProtectedApiKeyFilePath;
                if (File.Exists(path))
                {
                    try
                    {
                        byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                        _cachedKey = Encoding.UTF8.GetString(raw).Trim();
                        return _cachedKey;
                    }
                    catch (Exception ex)
                    {
                        // Ex.: pasta copiada de outro computador ou de outro usuário do Windows.
                        Logger.LogError("Saved Groq API key could not be unlocked on this computer; it must be entered again", ex);
                    }
                }

                string legacyKey = ReadLegacyKey();
                if (!string.IsNullOrEmpty(legacyKey))
                {
                    SaveApiKey(legacyKey); // lock reentrante: mesma thread
                    Logger.LogInfo("Groq API key migrated from the old unprotected storage.");
                    return legacyKey;
                }

                _cachedKey = string.Empty;
                return _cachedKey;
            }
        }

        /// <summary>Lê a chave das versões antigas: settings.json, api_key.txt ou credentials.dat.</summary>
        private static string ReadLegacyKey()
        {
            if (!string.IsNullOrWhiteSpace(SettingsService.LegacyApiKey))
                return SettingsService.LegacyApiKey.Trim();

            string plainPath = AppPathService.ApiKeyFilePath;
            if (File.Exists(plainPath))
            {
                try
                {
                    string key = File.ReadAllText(plainPath);
                    if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
                }
                catch { }
            }

            if (File.Exists(AppPathService.CredentialsFilePath))
            {
                try
                {
                    byte[] raw = DecryptLegacyBytes(File.ReadAllBytes(AppPathService.CredentialsFilePath));
                    if (raw != null)
                    {
                        string key = Encoding.UTF8.GetString(raw);
                        if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to migrate legacy credentials.dat", ex);
                }
            }

            return string.Empty;
        }

        /// <summary>Apaga as cópias desprotegidas da chave deixadas pelas versões antigas.</summary>
        private static void DeleteLegacyCopies()
        {
            try { if (File.Exists(AppPathService.ApiKeyFilePath)) File.Delete(AppPathService.ApiKeyFilePath); } catch { }
            try { if (File.Exists(AppPathService.CredentialsFilePath)) File.Delete(AppPathService.CredentialsFilePath); } catch { }

            if (SettingsService.LegacyApiKey != null)
            {
                // Regrava o settings.json, que deixa de ter o campo ApiKey.
                SettingsService.LegacyApiKey = null;
                SettingsService.Save();
            }
        }

        private static byte[] DecryptLegacyBytes(byte[] cipher)
        {
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = LegacyCryptoKey;
                    aes.IV = LegacyCryptoIv;
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
                    return ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
