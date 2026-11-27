using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public class PromptProfile
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string PromptText { get; set; }
        public bool IsBuiltIn { get; set; }
        public string UpdatedAt { get; set; }

        public PromptProfile()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "Novo Prompt";
            PromptText = "";
            IsBuiltIn = false;
            UpdatedAt = DateTime.UtcNow.ToString("o");
        }

        public PromptProfile(string id, string title, string promptText, bool isBuiltIn = false)
        {
            Id = id;
            Title = title;
            PromptText = promptText;
            IsBuiltIn = isBuiltIn;
            UpdatedAt = DateTime.UtcNow.ToString("o");
        }

        public override string ToString()
        {
            return Title;
        }
    }

    public class AppSettings
    {
        public string WhisperModel { get; set; }
        public string CorrectionModel { get; set; }
        public string SelectedPromptPreset { get; set; }
        public string CustomPromptText { get; set; }
        public string ActivePromptId { get; set; }
        public List<PromptProfile> PromptProfiles { get; set; }

        public bool AutoCopyToClipboard { get; set; }
        public bool AutoStartWithWindows { get; set; }
        public bool AutoRetryOnReconnect { get; set; }
        public bool ShowFloatingHud { get; set; }
        public bool ShowNotifications { get; set; }
        public bool AudioBeepFeedback { get; set; }
        public bool PortableMode { get; set; }
        
        public string HotkeyDisplay { get; set; }
        public bool HotkeyUseCtrl { get; set; }
        public bool HotkeyUseWin { get; set; }
        public bool HotkeyUseAlt { get; set; }
        public bool HotkeyUseShift { get; set; }
        public int HotkeyVirtualKey { get; set; }

        public bool PushToTalkMode { get; set; }
        public bool SilenceAutoStop { get; set; }
        public int SilenceAutoStopSeconds { get; set; }

        public bool EnableAudioPreprocessing { get; set; }
        public bool EnableFieldContext { get; set; }

        /// <summary>Sobe o volume de captação do microfone para 100% durante o ditado (e baixa se saturar).</summary>
        public bool BoostMicrophoneDuringDictation { get; set; }

        /// <summary>Procura atualizações ao abrir e uma vez por dia.</summary>
        public bool AutoCheckUpdates { get; set; }

        /// <summary>Última busca de atualizações (DateTime.UtcNow.Ticks); 0 = nunca.</summary>
        public long LastUpdateCheckTicks { get; set; }

        /// <summary>Nome do microfone escolhido; vazio usa o padrão do sistema.</summary>
        public string PreferredMicrophone { get; set; }

        /// <summary>Dias que o histórico de ditados é guardado. 0 = não guardar; -1 = sem limite.</summary>
        public int HistoryRetentionDays { get; set; }

        /// <summary>Versão das migrações de privacidade já aplicadas a este settings.json.</summary>
        public int PrivacyVersion { get; set; }

        public AppSettings()
        {
            WhisperModel = "whisper-large-v3";
            CorrectionModel = "openai/gpt-oss-120b";
            SelectedPromptPreset = "Transcrever em Parágrafos";
            CustomPromptText = "";
            ActivePromptId = "default_paragraphs";
            PromptProfiles = SettingsService.GetDefaultProfiles();

            AutoCopyToClipboard = true;
            AutoStartWithWindows = false;
            AutoRetryOnReconnect = true;
            ShowFloatingHud = true;
            ShowNotifications = true;
            AudioBeepFeedback = true;
            PortableMode = true;

            HotkeyDisplay = "F8";
            HotkeyUseCtrl = false;
            HotkeyUseWin = false;
            HotkeyUseAlt = false;
            HotkeyUseShift = false;
            HotkeyVirtualKey = 0x77; // VK_F8

            PushToTalkMode = false;
            SilenceAutoStop = false;
            SilenceAutoStopSeconds = 3;

            EnableAudioPreprocessing = true;
            EnableFieldContext = false;
            BoostMicrophoneDuringDictation = true;
            AutoCheckUpdates = true;
            LastUpdateCheckTicks = 0;
            PreferredMicrophone = "";
            HistoryRetentionDays = 7;
            PrivacyVersion = 0;
        }
    }

    public static class SettingsService
    {
        /// <summary>Perfil "Personalizado": o prompt escrito pela própria pessoa.</summary>
        public const string CustomPromptId = "custom";

        private const int CurrentPrivacyVersion = 1;

        private static AppSettings _currentSettings;

        /// <summary>
        /// Chave de API encontrada em texto puro num settings.json antigo. Fica só na memória até ser
        /// migrada para o armazenamento protegido; o settings.json não grava mais esse campo.
        /// </summary>
        public static string LegacyApiKey { get; set; }

        public static AppSettings Current
        {
            get
            {
                if (_currentSettings == null)
                    Load();
                return _currentSettings;
            }
        }

        public static List<PromptProfile> GetDefaultProfiles()
        {
            return new List<PromptProfile>
            {
                // As regras contra responder perguntas e inventar conteúdo ficam no TextCorrectionService
                // (valem para todos os modos). Aqui fica só o que muda de um modo para o outro.
                new PromptProfile(
                    "default_paragraphs",
                    "Transcrever em Parágrafos",
                    @"MODO PARÁGRAFOS:
- Corrija ortografia, acentuação, letras maiúsculas e pontuação.
- Separe o texto em parágrafos quando houver mudança de assunto, com uma linha em branco entre eles. Um ditado curto pode ficar num parágrafo só.
- Mantenha todas as palavras ditadas, na mesma ordem. A única troca permitida é corrigir palavras que o reconhecimento de voz trocou por outras de som parecido.
- Não resuma, não reescreva as frases, não crie títulos nem listas e não acrescente nada.",
                    true
                ),
                new PromptProfile(
                    "default_single_line",
                    "Transcrever em Linha Única",
                    @"MODO LINHA ÚNICA:
- Corrija ortografia, acentuação, letras maiúsculas e pontuação.
- Todo o texto fica em UMA ÚNICA LINHA contínua: nenhuma quebra de linha, nenhum parágrafo, nenhuma lista.
- Mantenha todas as palavras ditadas, na mesma ordem. A única troca permitida é corrigir palavras que o reconhecimento de voz trocou por outras de som parecido.
- Não resuma, não reescreva as frases e não acrescente nada.",
                    true
                )
            };
        }

        public static PromptProfile GetActivePrompt()
        {
            var s = Current;
            if (s.PromptProfiles == null || s.PromptProfiles.Count == 0)
            {
                s.PromptProfiles = GetDefaultProfiles();
                s.ActivePromptId = "default_paragraphs";
            }

            var found = s.PromptProfiles.FirstOrDefault(p => p.Id == s.ActivePromptId);
            if (found != null) return found;

            return s.PromptProfiles[0];
        }

        public static string GetActivePromptText()
        {
            var active = GetActivePrompt();
            if (active != null && !string.IsNullOrWhiteSpace(active.PromptText))
            {
                return active.PromptText;
            }

            var defaults = GetDefaultProfiles();
            return defaults[0].PromptText;
        }

        public static void SetActivePrompt(string promptId)
        {
            var s = Current;
            if (s.PromptProfiles != null && s.PromptProfiles.Any(p => p.Id == promptId))
            {
                s.ActivePromptId = promptId;
                s.SelectedPromptPreset = GetActivePrompt().Title;
                Save();
                Logger.LogInfo(string.Format("Active transcription mode changed to: {0}", promptId));
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(AppPathService.SettingsFilePath))
                {
                    string json = File.ReadAllText(AppPathService.SettingsFilePath);
                    var serializer = new JavaScriptSerializer();
                    _currentSettings = serializer.Deserialize<AppSettings>(json);

                    var raw = serializer.DeserializeObject(json) as Dictionary<string, object>;
                    object legacyKey;
                    if (raw != null && raw.TryGetValue("ApiKey", out legacyKey) && legacyKey is string && ((string)legacyKey).Trim().Length > 0)
                    {
                        LegacyApiKey = (string)legacyKey;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to load settings JSON. Using defaults.", ex);
            }

            if (_currentSettings == null)
            {
                _currentSettings = new AppSettings();
            }

            // Três modos: Parágrafos, Linha Única ou Personalizado (o prompt escrito pela pessoa)
            _currentSettings.PromptProfiles = GetDefaultProfiles();
            string custom = (_currentSettings.CustomPromptText ?? "").Trim();
            _currentSettings.CustomPromptText = custom;
            if (custom.Length > 0)
            {
                _currentSettings.PromptProfiles.Add(new PromptProfile(CustomPromptId, "Personalizado", custom));
            }
            bool customActive = _currentSettings.ActivePromptId == CustomPromptId && custom.Length > 0;
            if (_currentSettings.ActivePromptId != "default_single_line" && !customActive)
            {
                _currentSettings.ActivePromptId = "default_paragraphs";
            }
            _currentSettings.SelectedPromptPreset = GetActivePrompt().Title;

            // Default hotkey migration to F8
            if (_currentSettings.HotkeyVirtualKey == 0 || _currentSettings.HotkeyDisplay == "Ctrl + Win" || string.IsNullOrEmpty(_currentSettings.HotkeyDisplay))
            {
                _currentSettings.HotkeyDisplay = "F8";
                _currentSettings.HotkeyVirtualKey = 0x77;
                _currentSettings.HotkeyUseCtrl = false;
                _currentSettings.HotkeyUseWin = false;
                _currentSettings.HotkeyUseAlt = false;
                _currentSettings.HotkeyUseShift = false;
            }

            _currentSettings.WhisperModel = "whisper-large-v3";
            _currentSettings.CorrectionModel = "openai/gpt-oss-120b";
            _currentSettings.PortableMode = AppPathService.IsPortableMode;

            if (_currentSettings.PrivacyVersion < CurrentPrivacyVersion)
            {
                // Versões antigas gravavam o texto ditado nos logs e enviavam o texto do campo à Groq por padrão.
                Logger.DeleteAllLogs();
                _currentSettings.EnableFieldContext = false;
                _currentSettings.PrivacyVersion = CurrentPrivacyVersion;
            }

            Save();
        }

        public static void Save()
        {
            try
            {
                if (_currentSettings == null) return;

                _currentSettings.WhisperModel = "whisper-large-v3";
                _currentSettings.CorrectionModel = "openai/gpt-oss-120b";

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_currentSettings);
                File.WriteAllText(AppPathService.SettingsFilePath, json);
                Logger.LogInfo("Settings saved successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save settings JSON", ex);
            }
        }
    }
}
