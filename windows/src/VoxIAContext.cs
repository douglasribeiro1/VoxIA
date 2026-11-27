using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoxIA.Services;
using VoxIA.UI;

namespace VoxIA
{
    public class VoxIAContext : ApplicationContext
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _menuRecord;
        private ToolStripMenuItem _menuPrompts;
        private ToolStripMenuItem _menuModeParagraphs;
        private ToolStripMenuItem _menuModeSingleLine;
        private ToolStripMenuItem _menuDict;
        private ToolStripMenuItem _menuPending;
        private ToolStripMenuItem _menuHistory;
        private ToolStripMenuItem _menuShortcuts;
        private ToolStripMenuItem _menuSettings;
        private ToolStripMenuItem _menuAutoStart;
        private ToolStripMenuItem _menuExit;

        private AudioRecorder _audioRecorder;
        private FloatingHudOverlay _hud;
        private TargetContext _activeTargetContext;
        private LiveTranscriber _live;
        private UpdateService.UpdateInfo _pendingUpdate;
        private ToolStripMenuItem _menuUpdate;
        private System.Windows.Forms.Timer _updateTimer;
        private bool _balloonIsUpdate;
        private bool _updating;
        private bool _finishingDictation;
        private DateTime _lastActionTime = DateTime.MinValue;

        public VoxIAContext()
        {
            InitializeServices();
            InitializeTray();
            RegisterHotkeys();
            StartUpdateChecks();
            SettingsForm.RequestUpdateCheck = () => OnMenuUpdateClick();

            Logger.LogInfo("VoxIA Context fully initialized and resident in System Tray.");

            // Primeira vez (sem chave): mostra o passo a passo logo depois de o ícone aparecer na bandeja
            if (string.IsNullOrEmpty(SecureCredentialService.GetApiKey()))
            {
                var welcomeTimer = new System.Windows.Forms.Timer { Interval = 800 };
                welcomeTimer.Tick += (s, e) =>
                {
                    welcomeTimer.Stop();
                    welcomeTimer.Dispose();
                    ShowWelcomeGuide();
                };
                welcomeTimer.Start();
            }
        }

        /// <summary>Passo a passo para quem abre o VoxIA pela primeira vez e ainda não tem a chave da Groq.</summary>
        private void ShowWelcomeGuide()
        {
            string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";

            string msg = "Bem-vindo ao VoxIA!\n\n" +
                         "Para funcionar, o VoxIA precisa da SUA chave gratuita da Groq (o serviço que transforma a voz em texto). Leva 2 minutos:\n\n" +
                         "1. Clique em SIM: o site da Groq (console.groq.com/keys) vai abrir.\n" +
                         "2. Entre com sua conta Google (ou crie uma conta com e-mail). É grátis e não pede cartão.\n" +
                         "3. Clique em \"Create API Key\", dê um nome (ex.: VoxIA) e clique em \"Submit\".\n" +
                         "4. Copie a chave (começa com gsk_). Ela só aparece uma vez!\n" +
                         "5. Cole a chave na tela de Configurações do VoxIA e clique em Salvar.\n\n" +
                         string.Format("Pronto: clique onde quer escrever, aperte {0}, fale e aperte {0} de novo.\n\n", hotkey) +
                         "Abrir o site da Groq agora?";

            DialogResult r = MessageBox.Show(msg, "VoxIA — Primeiros passos", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start("https://console.groq.com/keys"); } catch { }
            }
            OpenSettingsForm();
        }

        private void InitializeServices()
        {
            AudioVolumeService.EnsureFullVolume();
            MicrophoneLevelService.RestoreAfterCrash();
            UpdateService.CleanupOldExe();

            _audioRecorder = new AudioRecorder();
            _hud = new FloatingHudOverlay(() => _audioRecorder.CurrentAudioLevel);

            RetryService.Start();
            RetryService.OnProcessingCompleted += OnProcessingSuccess;
            RetryService.OnProcessingFailed += OnProcessingFailure;
            RetryService.OnCorrectionWarning += OnCorrectionWarning;

            if (SettingsService.Current.AutoStartWithWindows)
            {
                // Auto-heal: Ensure registry Run key points to current portable executable path
                AutoStartService.SetAutoStart(true);
            }
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();

            string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";

            Theme.StyleContextMenu(_trayMenu);

            _menuRecord = new ToolStripMenuItem("Iniciar gravação", null, OnMenuRecordClick) { ShortcutKeyDisplayString = hotkey, Font = new Font(_trayMenu.Font, FontStyle.Bold) };
            _menuPrompts = new ToolStripMenuItem("Modo de transcrição");
            _menuModeParagraphs = new ToolStripMenuItem("Em parágrafos (com quebras de linha)", null, (s, e) => SetTranscriptionMode("default_paragraphs"));
            _menuModeSingleLine = new ToolStripMenuItem("Em linha única (sem quebras)", null, (s, e) => SetTranscriptionMode("default_single_line"));
            _menuPrompts.DropDown.Renderer = _trayMenu.Renderer;
            _menuPrompts.DropDownItems.Add(_menuModeParagraphs);
            _menuPrompts.DropDownItems.Add(_menuModeSingleLine);

            _menuDict = new ToolStripMenuItem("Dicionário de palavras", null, (s, e) => OpenDictionaryQuickForm()) { ShortcutKeyDisplayString = "Ctrl+Shift+W" };
            _menuPending = new ToolStripMenuItem("Áudios pendentes", null, (s, e) => OpenPendingForm());
            _menuHistory = new ToolStripMenuItem("Histórico e estatísticas", null, (s, e) => OpenHistoryForm());
            _menuShortcuts = new ToolStripMenuItem("Guia de atalhos...", null, (s, e) => ShowShortcutsDialog());
            _menuAutoStart = new ToolStripMenuItem("Iniciar com o Windows", null, OnMenuAutoStartClick);
            _menuSettings = new ToolStripMenuItem("Configurações...", null, (s, e) => OpenSettingsForm());
            _menuExit = new ToolStripMenuItem("Sair", null, OnMenuExitClick);

            var menuTitle = new ToolStripLabel("VoxIA  ·  Ditado por voz") { ForeColor = Theme.Navy, Font = new Font(_trayMenu.Font, FontStyle.Bold), Margin = new Padding(0, 4, 0, 4) };
            _trayMenu.Items.Add(menuTitle);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(new ToolStripMenuItem("Abrir o VoxIA", null, (s, e) => OpenMainWindow(SettingsForm.PageHome)));
            _trayMenu.Items.Add(_menuRecord);
            _trayMenu.Items.Add(_menuPrompts);
            _trayMenu.Items.Add(_menuDict);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_menuPending);
            _trayMenu.Items.Add(_menuHistory);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_menuShortcuts);
            _trayMenu.Items.Add(_menuAutoStart);
            _trayMenu.Items.Add(_menuSettings);
            _menuUpdate = new ToolStripMenuItem("Procurar atualizações", null, (s, e) => OnMenuUpdateClick());
            _trayMenu.Items.Add(_menuUpdate);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_menuExit);

            _trayMenu.Opening += (s, e) =>
            {
                UpdatePromptsMenu();
                UpdateAutoStartMenu();
            };

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _trayMenu,
                Text = string.Format("VoxIA — Ditado por Voz ({0})", hotkey),
                Visible = true
            };

            // Load high-resolution modern icon
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath, 32, 32);
                }
                else
                {
                    _notifyIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    OpenSettingsForm();
                }
            };
            _notifyIcon.DoubleClick += (s, e) => OpenMainWindow(SettingsForm.PageHome);
            UpdatePendingBadge();
            UpdatePromptsMenu();
            UpdateAutoStartMenu();
        }

        private void RegisterHotkeys()
        {
            GlobalHotkeyService.OnHotkeyPressed += OnHotkeyPressed;
            GlobalHotkeyService.OnHotkeyReleased += OnHotkeyReleased;
            GlobalHotkeyService.OnCancelPressed += OnCancelPressed;
            GlobalHotkeyService.OnDictionaryHotkeyPressed += OnDictionaryHotkeyPressed;
            GlobalHotkeyService.Start();
        }

        private void OnHotkeyPressed()
        {
            var s = SettingsService.Current;
            if (s.PushToTalkMode)
            {
                StartDictation();
            }
            else
            {
                // Toggle mode
                if (_audioRecorder.IsRecording)
                {
                    StopAndProcessDictation();
                }
                else
                {
                    StartDictation();
                }
            }
        }

        private void OnHotkeyReleased()
        {
            TextInsertionService.ReleaseAllModifiers();

            var s = SettingsService.Current;
            if (s.PushToTalkMode && _audioRecorder.IsRecording)
            {
                StopAndProcessDictation();
            }
        }

        private void OnCancelPressed()
        {
            TextInsertionService.ReleaseAllModifiers();

            if (_audioRecorder.IsRecording)
            {
                CancelLive();
                _audioRecorder.CancelRecording();
                MicrophoneLevelService.Restore();
                _hud.Hide();
                AudioFeedbackService.PlayStopBeep();
                ShowNotification("Gravação Cancelada", "A gravação atual foi descartada.");
            }
        }

        private void OnDictionaryHotkeyPressed()
        {
            string selectedText = null;
            string oldClipboard = ClipboardService.GetText();

            try
            {
                // 1. Clear clipboard temporarily to detect newly selected content
                ClipboardService.SetText("");
                System.Threading.Thread.Sleep(20);

                // 2. Perform clean atomic Ctrl + C keystroke
                TextInsertionService.SimulateCopyKeystroke();
                System.Threading.Thread.Sleep(60);

                string newlyCopied = ClipboardService.GetText();
                if (!string.IsNullOrWhiteSpace(newlyCopied))
                {
                    selectedText = newlyCopied.Trim();
                }
                else
                {
                    if (!string.IsNullOrEmpty(oldClipboard))
                        ClipboardService.SetText(oldClipboard);
                }
            }
            catch
            {
                selectedText = null;
            }

            using (var form = new QuickDictionaryForm(selectedText))
            {
                form.ShowDialog();
            }
        }

        private void OpenDictionaryQuickForm()
        {
            using (var form = new QuickDictionaryForm())
            {
                form.ShowDialog();
            }
        }

        private void ShowShortcutsDialog()
        {
            string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";

            string msg = "ATALHOS GLOBAIS DO VOXIA\n\n" +
                         string.Format("Iniciar / parar ditado:  {0}\n", hotkey) +
                         "   No modo push-to-talk, mantenha pressionado para falar.\n\n" +
                         "Adicionar palavra ao dicionário:  Ctrl + Shift + W  (ou Ctrl + Shift + D)\n" +
                         "   Selecione o texto em qualquer aplicativo (Word, navegador, e-mail) e pressione o atalho.\n\n" +
                         "Cancelar gravação em andamento:  Esc\n\n" +
                         "Configurações:  clique duplo no ícone do VoxIA na bandeja do Windows.";

            MessageBox.Show(msg, "Guia de Atalhos — VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnMenuRecordClick(object sender, EventArgs e)
        {
            if (_audioRecorder.IsRecording)
                StopAndProcessDictation();
            else
                StartDictation();
        }

        private void CancelLive()
        {
            if (_live != null)
            {
                _live.Cancel();
                _live = null;
            }
        }

        private void StartDictation()
        {
            if (_audioRecorder.IsRecording || _finishingDictation) return;

            // Debounce guard: 400ms between actions
            double msSinceLast = (DateTime.Now - _lastActionTime).TotalMilliseconds;
            if (msSinceLast < 400) return;
            _lastActionTime = DateTime.Now;

            string apiKey = SecureCredentialService.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowWelcomeGuide();
                return;
            }

            try
            {
                // Capture active target window right before recording
                _activeTargetContext = TargetAppTracker.CaptureActiveContext();

                // Microfone no máximo durante o ditado (baixa sozinho se saturar; volta ao parar)
                MicrophoneLevelService.Boost(SettingsService.Current.PreferredMicrophone);

                // Start recording PCM stream
                _audioRecorder.StartRecording();
                AudioFeedbackService.PlayStartBeep();

                // Blocos longos já vão sendo transcritos enquanto a pessoa fala
                CancelLive();
                _live = new LiveTranscriber(_audioRecorder, _activeTargetContext != null ? _activeTargetContext.ContextText : null);
                _live.Start();

                _hud.ShowRecording();
                string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";
                _menuRecord.Text = "Parar e enviar";
                _menuRecord.ShortcutKeyDisplayString = hotkey;
            }
            catch (Exception ex)
            {
                MicrophoneLevelService.Restore();
                _hud.ShowError(ex.Message);
                ShowNotification("Erro ao Iniciar Gravação", ex.Message);
            }
        }

        private async void StopAndProcessDictation()
        {
            if (!_audioRecorder.IsRecording) return;

            // Debounce guard: 400ms between actions
            double msSinceLast = (DateTime.Now - _lastActionTime).TotalMilliseconds;
            if (msSinceLast < 400) return;
            _lastActionTime = DateTime.Now;

            double durationSec = _audioRecorder.RecordingDuration.TotalSeconds;
            bool captured = _audioRecorder.StopCapture();
            MicrophoneLevelService.Restore();
            string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";
            _menuRecord.Text = "Iniciar gravação";
            _menuRecord.ShortcutKeyDisplayString = hotkey;
            AudioFeedbackService.PlayStopBeep();

            if (!captured)
            {
                CancelLive();
                _hud.ShowError("Falha no arquivo de áudio");
                return;
            }

            _hud.ShowProcessing();

            // Junta o que já foi transcrito durante a fala; o arquivo fica só com o restante
            LiveTranscriber.Partial partial = null;
            string filePath;
            _finishingDictation = true;
            try
            {
                if (_live != null)
                {
                    LiveTranscriber live = _live;
                    _live = null;
                    partial = await live.FinishAsync();
                }
                filePath = _audioRecorder.SaveCaptured(partial != null ? partial.CommittedSamples : 0);
            }
            finally
            {
                _finishingDictation = false;
            }
            string prefix = partial != null ? partial.Text : null;
            bool hasPrefix = !string.IsNullOrWhiteSpace(prefix);

            if (!hasPrefix && (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)))
            {
                _hud.ShowError("Falha no arquivo de áudio");
                return;
            }

            // Sem fala: não envia nada ao Whisper (ele inventaria palavras)
            if (!hasPrefix && _audioRecorder.LastRecordingWasSilent)
            {
                Logger.LogInfo("Recording had no speech; not sent to Whisper.");
                AudioChunker.DeleteParts(filePath);
                try { File.Delete(filePath); } catch { }
                _hud.Hide();
                return;
            }

            // CRITICAL RULE: Save recording to persistent queue BEFORE any network upload attempt!
            var queueItem = PendingAudioRepository.AddRecording(filePath, durationSec, _activeTargetContext,
                prefix, _audioRecorder.LastRecordingWasSilent, _audioRecorder.LastSpeechFrames);
            UpdatePendingBadge();

            // Process item through async queue worker
            await RetryService.ProcessItemAsync(queueItem);
            UpdatePendingBadge();
        }

        private void OnProcessingSuccess(string rawText, string finalText)
        {
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                _hud.ShowSuccess();
            }
            else
            {
                _hud.Hide();
            }
            UpdatePendingBadge();
        }

        private void OnCorrectionWarning(string reason)
        {
            ShowNotification("Texto colado sem a correção da IA",
                "Foi usado o texto da transcrição, sem revisão nem formatação. Motivo: " + reason + ".");
        }

        private void OnProcessingFailure(string error)
        {
            _hud.ShowError(error);
            UpdatePendingBadge();

            if (SettingsService.Current.ShowNotifications)
            {
                ShowNotification("Falha no Envio — Áudio Salvo", string.Format("O áudio foi salvo localmente e será reenviado quando a conexão voltar. ({0})", error));
            }
        }

        private void UpdatePendingBadge()
        {
            var pending = PendingAudioRepository.GetPendingItems();
            int count = pending != null ? pending.Count : 0;

            if (count > 0)
            {
                _menuPending.Text = string.Format("Áudios pendentes ({0})", count);
                _menuPending.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            }
            else
            {
                _menuPending.Text = "Áudios pendentes";
                _menuPending.Font = new Font(_trayMenu.Font, FontStyle.Regular);
            }
        }

        private void UpdatePromptsMenu()
        {
            if (_menuModeParagraphs == null || _menuModeSingleLine == null) return;

            bool isSingleLine = SettingsService.Current.ActivePromptId == "default_single_line";
            _menuModeParagraphs.Checked = !isSingleLine;
            _menuModeSingleLine.Checked = isSingleLine;
        }

        private void SetTranscriptionMode(string modeId)
        {
            SettingsService.SetActivePrompt(modeId);
            UpdatePromptsMenu();
            string modeName = (modeId == "default_single_line") ? "Linha Única (sem quebras)" : "Parágrafos (com quebras de linha)";
            ShowNotification("Modo de Transcrição", "Modo ativo alterado para: " + modeName);
        }

        private void ShowNotification(string title, string text)
        {
            if (_notifyIcon != null && SettingsService.Current.ShowNotifications)
            {
                _balloonIsUpdate = false;
                _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
        }

        // ---------- Atualização com um clique ----------

        private void StartUpdateChecks()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.BalloonTipClicked += (s, e) => { if (_balloonIsUpdate) PromptUpdate(); };
            }
            if (Program.JustUpdated)
            {
                ShowNotification("VoxIA atualizado", "A versão nova já está funcionando.");
            }
            // Primeira busca 1 minuto depois de abrir; depois confere a cada hora se já passou um dia
            _updateTimer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            _updateTimer.Tick += async (s, e) =>
            {
                _updateTimer.Interval = 60 * 60 * 1000;
                if (_pendingUpdate != null || !UpdateService.IsAutoCheckDue()) return;
                var info = await UpdateService.CheckAsync();
                if (info != null) OnUpdateFound(info);
            };
            _updateTimer.Start();
        }

        private void OnUpdateFound(UpdateService.UpdateInfo info)
        {
            _pendingUpdate = info;
            _menuUpdate.Text = "Atualizar o VoxIA (versão nova)";
            _menuUpdate.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            if (_notifyIcon != null)
            {
                _balloonIsUpdate = true;
                _notifyIcon.ShowBalloonTip(10000, "Versão nova do VoxIA", "Clique aqui para atualizar (leva alguns segundos).", ToolTipIcon.Info);
            }
        }

        private async void OnMenuUpdateClick()
        {
            if (_pendingUpdate != null)
            {
                PromptUpdate();
                return;
            }
            if (!UpdateService.IsSupported)
            {
                MessageBox.Show("Esta cópia do VoxIA foi compilada à mão e não se atualiza sozinha. Baixe a versão nova pelo link do GitHub.", "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var info = await UpdateService.CheckAsync();
            if (info == null)
            {
                MessageBox.Show(string.Format("Você já está com a versão mais nova do VoxIA (build {0}).", BuildInfo.Build), "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OnUpdateFound(info);
            PromptUpdate();
        }

        private async void PromptUpdate()
        {
            if (_pendingUpdate == null || _updating) return;
            if (_audioRecorder.IsRecording || _finishingDictation)
            {
                MessageBox.Show("Termine o ditado em andamento e depois clique em atualizar.", "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var answer = MessageBox.Show(
                "Há uma versão nova do VoxIA.\n\nAtualizar agora? O VoxIA fecha e abre de novo sozinho em alguns segundos. Sua chave, dicionário e configurações continuam iguais.",
                "Atualizar o VoxIA", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            _updating = true;
            _menuUpdate.Text = "Atualizando…";
            _menuUpdate.Enabled = false;
            bool restarting = await UpdateService.ApplyAsync(_pendingUpdate);
            if (restarting)
            {
                OnMenuExitClick(this, EventArgs.Empty);
                return;
            }
            _updating = false;
            _menuUpdate.Text = "Atualizar o VoxIA (versão nova)";
            _menuUpdate.Enabled = true;
        }

        private void UpdateAutoStartMenu()
        {
            if (_menuAutoStart != null)
            {
                _menuAutoStart.Checked = AutoStartService.IsAutoStartEnabled();
            }
        }

        private void OnMenuAutoStartClick(object sender, EventArgs e)
        {
            bool newState = !AutoStartService.IsAutoStartEnabled();
            AutoStartService.SetAutoStart(newState);
            SettingsService.Current.AutoStartWithWindows = newState;
            SettingsService.Save();
            if (_menuAutoStart != null)
            {
                _menuAutoStart.Checked = newState;
            }

            ShowNotification(
                "Inicialização com o Windows",
                newState
                    ? "VoxIA configurado para iniciar automaticamente com o Windows."
                    : "Inicialização automática com o Windows desativada."
            );
        }

        private SettingsForm _activeSettingsForm = null;

        private void OpenSettingsForm()
        {
            OpenMainWindow(SettingsForm.PageSettings);
        }

        /// <summary>Abre (ou traz para a frente) a janela principal, já na página pedida.</summary>
        private void OpenMainWindow(int page)
        {
            if (_activeSettingsForm != null && !_activeSettingsForm.IsDisposed)
            {
                if (_activeSettingsForm.WindowState == FormWindowState.Minimized)
                    _activeSettingsForm.WindowState = FormWindowState.Normal;
                _activeSettingsForm.SelectTab(page);
                _activeSettingsForm.BringToFront();
                _activeSettingsForm.Activate();
                return;
            }

            _activeSettingsForm = new SettingsForm();
            _activeSettingsForm.FormClosed += (s, e) =>
            {
                _activeSettingsForm = null;
                UpdatePromptsMenu();
                UpdateAutoStartMenu();
                UpdatePendingBadge();
            };
            _activeSettingsForm.SelectTab(page);
            _activeSettingsForm.Show();
            _activeSettingsForm.BringToFront();
            _activeSettingsForm.Activate();
        }

        private void OpenPendingForm()
        {
            OpenMainWindow(SettingsForm.PagePending);
        }

        private void OpenHistoryForm()
        {
            OpenMainWindow(SettingsForm.PageHistory);
        }

        private void OnMenuExitClick(object sender, EventArgs e)
        {
            MicrophoneLevelService.Restore();
            GlobalHotkeyService.Stop();
            RetryService.Stop();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GlobalHotkeyService.Stop();
                RetryService.Stop();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
