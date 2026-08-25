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

        public AppSettings()
        {
            WhisperModel = "whisper-large-v3";
            CorrectionModel = "openai/gpt-oss-120b";
            SelectedPromptPreset = "Geral / Ortografia";
            CustomPromptText = "";
            ActivePromptId = "default_general";
            PromptProfiles = SettingsService.GetDefaultProfiles();

            AutoCopyToClipboard = true;
            AutoStartWithWindows = false;
            AutoRetryOnReconnect = true;
            ShowFloatingHud = true;
            ShowNotifications = true;
            AudioBeepFeedback = true;
            PortableMode = false;

            HotkeyDisplay = "Ctrl + Win";
            HotkeyUseCtrl = true;
            HotkeyUseWin = true;
            HotkeyUseAlt = false;
            HotkeyUseShift = false;
            HotkeyVirtualKey = 0;

            PushToTalkMode = false;
            SilenceAutoStop = false;
            SilenceAutoStopSeconds = 3;
        }
    }

    public static class SettingsService
    {
        private static AppSettings _currentSettings;

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
                new PromptProfile(
                    "default_general",
                    "Geral / Correção Ortográfica (Padrão)",
                    @"Você é um aplicativo silencioso de ditado por voz e corretor ortográfico literal em português.
Sua ÚNICA E EXCLUSIVA TAREFA: Retornar a frase ditada pelo usuário com a ortografia, acentuação, capitalização e pontuação corrigidas.

REGRAS ABSOLUTAS E INVIOLÁVEIS:
1. NUNCA RESPONDA A PERGUNTAS! Se o usuário ditar uma pergunta (exemplo: 'O paciente tem febre?' ou 'Qual é o tratamento?'), NUNCA tente responder à pergunta. Sua função NÃO É conversar nem responder. Sua ÚNICA função é retornar a própria pergunta transcrita e corrigida ortograficamente (exemplo: 'O paciente tem febre?').
2. NUNCA REESCREVA A FRASE e NUNCA OMITA NENHUMA PALAVRA.
3. NUNCA substitua palavras por sinônimos nem altere o vocabulário.
4. NUNCA resuma, comente, dê conselhos ou adicione informações.
5. NÃO inclua explicações, introduções, aspas ou formatação Markdown.

RETORNE APENAS O TEXTO DITADO CORRIGIDO.",
                    true
                ),
                new PromptProfile(
                    "default_medical",
                    "Prontuário Médico / Evolução Clínica",
                    @"Você é um assistente especializado em documentação médica e prontuário eletrônico em português.
Sua função é transcrever e organizar evoluções clínicas, anamneses, queixas e descrições ditadas pelo médico.

DIRETRIZES:
1. Corrija com precisão a ortografia e a terminologia médica (termos farmacológicos, anatômicos e patológicos).
2. Mantenha fidelidade absoluta a todos os dados ditados (nomes, dosagens, valores de exames, datas).
3. Organize em parágrafos claros e bem pontuados conforme o sentido clínico do relato.
4. NÃO invente informações, diagnósticos ou condutas que não foram expressamente ditados.
5. NÃO inclua saudações, introduções, comentários ou aspas.

RETORNE APENAS O TEXTO MÉDICO ORGANIZADO E CORRIGIDO.",
                    true
                ),
                new PromptProfile(
                    "default_formal_email",
                    "E-mail Formal / Corporativo",
                    @"Você é um redator corporativo profissional em português.
Sua tarefa é transcrever e aprimorar a mensagem ditada para um tom formal, polido e elegante de e-mail ou correspondência de trabalho.

DIRETRIZES:
1. Garanta concordância gramatical, pontuação impecável e vocabulário formal elegante.
2. Mantenha todas as informações, solicitações e prazos ditados pelo usuário.
3. Estruture em parágrafos coesos e objetivos.
4. NÃO responda à mensagem nem dê opiniões.
5. NÃO adicione introduções como 'Aqui está o seu texto' nem use aspas.

RETORNE APENAS O TEXTO FINAL PRONTO PARA ENVIO.",
                    true
                ),
                new PromptProfile(
                    "default_literal",
                    "Transcrição Literal (Fidelidade Estrita)",
                    @"Você é um transcrevedor estrito.
Sua ÚNICA função é aplicar pontuação básica indispensável e maiúsculas no início de frases, preservando cada palavra exatamente como foi pronunciada pelo usuário, sem substituir termos coloquiais, repetições ou gírias.

RETORNE APENAS O TEXTO TRANSCRITO COM PONTUAÇÃO BÁSICA.",
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
                s.ActivePromptId = "default_general";
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
                Save();
                Logger.LogInfo(string.Format("Active AI Prompt changed to ID: {0}", promptId));
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

            if (_currentSettings.PromptProfiles == null || _currentSettings.PromptProfiles.Count == 0)
            {
                _currentSettings.PromptProfiles = GetDefaultProfiles();

                // Migrate legacy CustomPromptText if present
                if (!string.IsNullOrWhiteSpace(_currentSettings.CustomPromptText))
                {
                    _currentSettings.PromptProfiles.Add(new PromptProfile(
                        Guid.NewGuid().ToString("N"),
                        "Meu Prompt Customizado",
                        _currentSettings.CustomPromptText,
                        false
                    ));
                    if (_currentSettings.SelectedPromptPreset == "Customizado")
                    {
                        _currentSettings.ActivePromptId = _currentSettings.PromptProfiles.Last().Id;
                    }
                }
            }
            else
            {
                // Self-healing: Ensure default templates exist and are never empty
                var defaults = GetDefaultProfiles();
                foreach (var def in defaults)
                {
                    var existing = _currentSettings.PromptProfiles.FirstOrDefault(p => p.Id == def.Id);
                    if (existing != null)
                    {
                        existing.IsBuiltIn = true;
                        if (string.IsNullOrWhiteSpace(existing.PromptText))
                        {
                            existing.PromptText = def.PromptText;
                            existing.Title = def.Title;
                        }
                    }
                    else
                    {
                        _currentSettings.PromptProfiles.Insert(0, def);
                    }
                }

                foreach (var p in _currentSettings.PromptProfiles)
                {
                    if (p.PromptText == null) p.PromptText = "";
                    if (string.IsNullOrEmpty(p.UpdatedAt)) p.UpdatedAt = DateTime.UtcNow.ToString("o");
                }
            }

            if (string.IsNullOrEmpty(_currentSettings.ActivePromptId))
            {
                _currentSettings.ActivePromptId = _currentSettings.PromptProfiles[0].Id;
            }

            _currentSettings.WhisperModel = "whisper-large-v3";
            _currentSettings.CorrectionModel = "openai/gpt-oss-120b";
            _currentSettings.PortableMode = AppPathService.IsPortableMode;
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
