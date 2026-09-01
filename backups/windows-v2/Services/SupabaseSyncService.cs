using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public static class SupabaseSyncService
    {
        public const string SupabaseUrl = "https://hbtbykhhkrtbfnznnxqa.supabase.co";
        public const string SupabaseAnonKey = "sb_publishable_1P6EmICwhLs8MmVKMOBdsA_pbjH2WBI";

        public static bool IsLoggedIn
        {
            get { return !string.IsNullOrEmpty(SettingsService.Current.SupabaseAccessToken); }
        }

        public static string UserEmail
        {
            get { return SettingsService.Current.SupabaseUserEmail ?? string.Empty; }
        }

        public static event Action<bool, string> OnSyncStatusChanged;

        /// <summary>
        /// Autentica o usuário no Supabase com E-mail e Senha
        /// </summary>
        public static async Task<bool> LoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("E-mail e senha não podem estar em branco.");

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(25);
                    client.DefaultRequestHeaders.Add("apikey", SupabaseAnonKey);

                    var payload = new
                    {
                        email = email.Trim(),
                        password = password
                    };

                    var serializer = new JavaScriptSerializer();
                    string json = serializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    string endpoint = string.Format("{0}/auth/v1/token?grant_type=password", SupabaseUrl);
                    HttpResponseMessage resp = await client.PostAsync(endpoint, content);
                    string respBody = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        Logger.LogError("Supabase Login Failed: " + respBody);
                        throw new InvalidOperationException("Erro ao fazer login no Supabase: " + respBody);
                    }

                    var dict = serializer.Deserialize<Dictionary<string, object>>(respBody);
                    string accessToken = dict.ContainsKey("access_token") ? dict["access_token"].ToString() : "";
                    string refreshToken = dict.ContainsKey("refresh_token") ? dict["refresh_token"].ToString() : "";

                    string userId = "";
                    if (dict.ContainsKey("user"))
                    {
                        var userObj = dict["user"] as Dictionary<string, object>;
                        if (userObj != null && userObj.ContainsKey("id"))
                            userId = userObj["id"].ToString();
                    }


                    SettingsService.Current.SupabaseAccessToken = accessToken;
                    SettingsService.Current.SupabaseRefreshToken = refreshToken;
                    SettingsService.Current.SupabaseUserEmail = email.Trim();
                    SettingsService.Current.SupabaseUserId = userId;
                    SettingsService.Save();

                    Logger.LogInfo("Supabase Login successful for user: " + email);
                    
                    // Sincroniza imediatamente após o login
                    await SyncNowAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Supabase Login Exception", ex);
                throw;
            }
        }

        public static void Logout()
        {
            SettingsService.Current.SupabaseAccessToken = "";
            SettingsService.Current.SupabaseRefreshToken = "";
            SettingsService.Current.SupabaseUserEmail = "";
            SettingsService.Current.SupabaseUserId = "";
            SettingsService.Save();
            Logger.LogInfo("Supabase Logged out.");
        }

        /// <summary>
        /// Sincroniza Prompts e Dicionário entre o cliente Windows e o Supabase
        /// </summary>
        public static async Task<string> SyncNowAsync()
        {
            if (!IsLoggedIn)
                return "Não autenticado no Supabase.";

            string token = SettingsService.Current.SupabaseAccessToken;

            try
            {
                int promptsCount = 0;
                int dictCount = 0;

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("apikey", SupabaseAnonKey);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var serializer = new JavaScriptSerializer();

                    // 1. Download de Prompts da Nuvem
                    try
                    {
                        string promptUrl = string.Format("{0}/rest/v1/prompts?select=*", SupabaseUrl);
                        HttpResponseMessage pResp = await client.GetAsync(promptUrl);
                        if (pResp.IsSuccessStatusCode)
                        {
                            string pJson = await pResp.Content.ReadAsStringAsync();
                            var cloudPrompts = serializer.Deserialize<List<Dictionary<string, object>>>(pJson);
                            if (cloudPrompts != null && cloudPrompts.Count > 0)
                            {
                                var list = new List<PromptProfile>();
                                foreach (var item in cloudPrompts)
                                {
                                    string id = item.ContainsKey("id") ? item["id"].ToString() : Guid.NewGuid().ToString("N");
                                    string title = item.ContainsKey("title") ? item["title"].ToString() : "Prompt";
                                    string text = item.ContainsKey("prompt_text") ? item["prompt_text"].ToString() : "";
                                    bool isBuiltIn = item.ContainsKey("is_builtin") && Convert.ToBoolean(item["is_builtin"]);
                                    list.Add(new PromptProfile(id, title, text, isBuiltIn));
                                }

                                if (list.Count > 0)
                                {
                                    SettingsService.Current.PromptProfiles = list;
                                    SettingsService.Save();
                                    promptsCount = list.Count;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Erro ao sincronizar Prompts", ex);
                    }

                    // 2. Download do Dicionário da Nuvem
                    try
                    {
                        string dictUrl = string.Format("{0}/rest/v1/dictionary?select=*", SupabaseUrl);
                        HttpResponseMessage dResp = await client.GetAsync(dictUrl);
                        if (dResp.IsSuccessStatusCode)
                        {
                            string dJson = await dResp.Content.ReadAsStringAsync();
                            var cloudDict = serializer.Deserialize<List<Dictionary<string, object>>>(dJson);
                            if (cloudDict != null && cloudDict.Count > 0)
                            {
                                var list = new List<DictionaryEntry>();
                                foreach (var item in cloudDict)
                                {
                                    string id = item.ContainsKey("id") ? item["id"].ToString() : Guid.NewGuid().ToString("N");
                                    string orig = item.ContainsKey("original") ? item["original"].ToString() : "";
                                    string repl = item.ContainsKey("replacement") ? item["replacement"].ToString() : "";
                                    bool isVocab = item.ContainsKey("is_vocabulary_only") && Convert.ToBoolean(item["is_vocabulary_only"]);
                                    list.Add(new DictionaryEntry(orig, repl, isVocab) { Id = id });
                                }

                                if (list.Count > 0)
                                {
                                    DictionaryService.SetEntries(list);
                                    dictCount = list.Count;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Erro ao sincronizar Dicionário", ex);
                    }
                }

                string msg = string.Format("✓ Sincronizado com sucesso! ({0} prompts, {1} termos)", promptsCount, dictCount);
                if (OnSyncStatusChanged != null)
                    OnSyncStatusChanged(true, msg);

                return msg;
            }
            catch (Exception ex)
            {
                string msg = "Falha na sincronização: " + ex.Message;
                Logger.LogError("Supabase Sync Failed", ex);
                if (OnSyncStatusChanged != null)
                    OnSyncStatusChanged(false, msg);
                return msg;
            }
        }
    }
}
