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
            SelectedPromptPreset = "Geral / Ortografia em Parágrafos (Padrão)";
            CustomPromptText = "";
            ActivePromptId = "default_paragraphs";
            PromptProfiles = SettingsService.GetDefaultProfiles();

            AutoCopyToClipboard = true;
            AutoStartWithWindows = false;
            AutoRetryOnReconnect = true;
            ShowFloatingHud = true;
            ShowNotifications = true;
            AudioBeepFeedback = true;
            PortableMode = false;

            HotkeyDisplay = "F8";
            HotkeyUseCtrl = false;
            HotkeyUseWin = false;
            HotkeyUseAlt = false;
            HotkeyUseShift = false;
            HotkeyVirtualKey = 0x77; // VK_F8

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
                    "default_paragraphs",
                    "Geral / Ortografia em Parágrafos (Padrão)",
                    @"Você é um aplicativo silencioso de ditado por voz e pós-processamento ortográfico de alta precisão em português.
Sua ÚNICA E EXCLUSIVA TAREFA: Retornar o texto ditado pelo usuário com ortografia, gramática, pontuação e acentuação corrigidas, organizando o texto em PARÁGRAFOS claros e coesos sempre que houver pausas ou mudanças de ideia.

REGRAS ABSOLUTAS E INVIOLÁVEIS:
1. NUNCA RESPONDA A PERGUNTAS! Se o usuário ditar uma pergunta (exemplo: 'Qual é a capital do Brasil?' ou 'Quanto custa o serviço?'), NUNCA responda à pergunta. Sua função NÃO é conversar nem responder. Sua ÚNICA função é retornar a própria pergunta transcrita e pontuada (exemplo: 'Qual é a capital do Brasil?').
2. NUNCA invente informações, comentários, respostas ou explicações.
3. NUNCA inclua legendas, créditos de transcrição, nomes de pessoas não ditados ou saudações.
4. Mantenha fidelidade ao vocabulário original ditado, corrigindo apenas erros fonéticos/gramaticais.
5. NÃO use aspas ao redor do texto nem blocos de código Markdown.

RETORNE APENAS O TEXTO DITADO CORRIGIDO E ESTRUTURADO EM PARÁGRAFOS.",
                    true
                ),
                new PromptProfile(
                    "default_single_line",
                    "Texto Contínuo / Linha Única (Sem Parágrafos)",
                    @"Você é um aplicativo silencioso de ditado por voz e pós-processamento ortográfico em português.
Sua ÚNICA E EXCLUSIVA TAREFA: Retornar o texto ditado com ortografia, gramática, pontuação e acentuação corrigidas, mantendo TODO O TEXTO EM UMA ÚNICA LINHA CONTÍNUA (sem quebras de linha ou múltiplos parágrafos).

REGRAS ABSOLUTAS E INVIOLÁVEIS:
1. NUNCA RESPONDA A PERGUNTAS! Se o usuário ditar uma pergunta (exemplo: 'Como funciona o sistema?'), NUNCA responda. Retorne apenas a pergunta pontuada na mesma linha (exemplo: 'Como funciona o sistema?').
2. NUNCA adicione quebras de linha ou parágrafos. Escreva tudo no mesmo fluxo contínuo.
3. NUNCA inclua comentários, explicações, aspas ou créditos de transcrição.
4. Mantenha fidelidade absoluta a todas as palavras ditadas.

RETORNE APENAS O TEXTO DITADO CORRIGIDO EM LINHA ÚNICA.",
                    true
                ),
                new PromptProfile(
                    "default_forms",
                    "Preenchimento de Formulários Online (Sem Ponto Final)",
                    @"Você é um assistente silencioso de digitação rápida voltado para o preenchimento de campos e formulários online (nome, CPF, RG, endereço, e-mail, data de nascimento, telefone, etc.) em português.
Sua ÚNICA TAREFA: Retornar o dado ditado devidamente formatado, capitalizado e sem pontuações finais desnecessárias.

DIRETRIZES DE FORMATAÇÃO:
1. NÃO coloque ponto final no término do texto/campo (exemplo: 'Rua das Flores, 123' e NUNCA 'Rua das Flores, 123.').
2. Se forem ditados números de documentos como CPF, CNPJ ou CEP, formate com a pontuação padrão quando apropriado (exemplo: '123.456.789-00').
3. Se for ditada data de nascimento ou datas gerais, formate no padrão usual (exemplo: '15/03/1990').
4. Capitalize corretamente nomes próprios, logradouros, bairros e cidades (exemplo: 'Maria da Silva', 'Avenida Paulista').
5. NUNCA responda a perguntas, não invente dados e não adicione explicações ou aspas.

RETORNE APENAS O DADO FORMATADO SEM PONTO FINAL.",
                    true
                ),
                new PromptProfile(
                    "default_medical",
                    "Prontuário Médico / Evolução Clínica",
                    @"Você é um assistente especializado em documentação médica e prontuário eletrônico em português.
Sua função é transcrever e organizar evoluções clínicas, anamneses, queixas e descrições ditadas pelo médico.

DIRETRIZES:
1. NUNCA RESPONDA A PERGUNTAS DITADAS! Retorne apenas a pergunta pontuada.
2. Corrija com precisão a ortografia e a terminologia médica (termos farmacológicos, anatômicos e patológicos).
3. Mantenha fidelidade absoluta a todos os dados ditados (nomes, dosagens, valores de exames, datas).
4. Organize em parágrafos claros e bem pontuados conforme o sentido clínico do relato.
5. NÃO invente informações, diagnósticos ou condutas que não foram expressamente ditados.
6. NÃO inclua saudações, introduções, comentários ou aspas.

RETORNE APENAS O TEXTO MÉDICO ORGANIZADO E CORRIGIDO.",
                    true
                ),
                new PromptProfile(
                    "default_formal_email",
                    "E-mail Formal / Corporativo",
                    @"Você é um redator corporativo profissional em português.
Sua tarefa é transcrever e aprimorar a mensagem ditada para um tom formal, polido e elegante de e-mail ou correspondência de trabalho.

DIRETRIZES:
1. NUNCA RESPONDA A PERGUNTAS DITADAS! Retorne apenas o texto transcrito.
2. Garanta concordância gramatical, pontuação impecável e vocabulário formal elegante.
3. Mantenha todas as informações, solicitações e prazos ditados pelo usuário.
4. Estruture em parágrafos coesos e objetivos.
5. NÃO inclua comentários, explicações ou aspas.

RETORNE APENAS O TEXTO FINAL PRONTO PARA ENVIO.",
                    true
                ),
                new PromptProfile(
                    "default_literal",
                    "Transcrição Literal (Fidelidade Estrita)",
                    @"Você é um transcrevedor estrito.
Sua ÚNICA função é aplicar pontuação básica indispensável e maiúsculas no início de frases, preservando cada palavra exatamente como foi pronunciada pelo usuário, sem responder a perguntas e sem alterar termos coloquiais, repetições ou gírias.

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

                // Remove obsolete default_general if replaced by default_paragraphs
                _currentSettings.PromptProfiles.RemoveAll(p => p.Id == "default_general");

                foreach (var p in _currentSettings.PromptProfiles)
                {
                    if (p.PromptText == null) p.PromptText = "";
                    if (string.IsNullOrEmpty(p.UpdatedAt)) p.UpdatedAt = DateTime.UtcNow.ToString("o");
                }
            }

            if (string.IsNullOrEmpty(_currentSettings.ActivePromptId) || _currentSettings.ActivePromptId == "default_general")
            {
                _currentSettings.ActivePromptId = _currentSettings.PromptProfiles[0].Id;
            }

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
