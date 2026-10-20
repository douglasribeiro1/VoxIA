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
        private DateTime _lastActionTime = DateTime.MinValue;

        public VoxIAContext()
        {
            InitializeServices();
            InitializeTray();
            RegisterHotkeys();

            Logger.LogInfo("VoxIA Context fully initialized and resident in System Tray.");
        }

        private void InitializeServices()
        {
            AudioVolumeService.EnsureFullVolume();

            _audioRecorder = new AudioRecorder();
            _hud = new FloatingHudOverlay(() => _audioRecorder.CurrentAudioLevel);

            RetryService.Start();
            RetryService.OnProcessingCompleted += OnProcessingSuccess;
            RetryService.OnProcessingFailed += OnProcessingFailure;

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
            _notifyIcon.DoubleClick += (s, e) => OpenSettingsForm();
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
                _audioRecorder.CancelRecording();
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
                         "   Selecione o texto em qualquer aplicativo (Word, navegador, prontuário) e pressione o atalho.\n\n" +
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

        private void StartDictation()
        {
            if (_audioRecorder.IsRecording) return;

            // Debounce guard: 400ms between actions
            double msSinceLast = (DateTime.Now - _lastActionTime).TotalMilliseconds;
            if (msSinceLast < 400) return;
            _lastActionTime = DateTime.Now;

            string apiKey = SecureCredentialService.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowNotification("API Key Ausente", "Por favor, configure sua API Key da Groq nas Configurações.");
                OpenSettingsForm();
                return;
            }

            try
            {
                // Capture active target window right before recording
                _activeTargetContext = TargetAppTracker.CaptureActiveContext();

                // Start recording PCM stream
                _audioRecorder.StartRecording();
                AudioFeedbackService.PlayStartBeep();

                _hud.ShowRecording();
                string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";
                _menuRecord.Text = "Parar e enviar";
                _menuRecord.ShortcutKeyDisplayString = hotkey;
            }
            catch (Exception ex)
            {
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
            string filePath = _audioRecorder.StopRecording();
            string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";
            _menuRecord.Text = "Iniciar gravação";
            _menuRecord.ShortcutKeyDisplayString = hotkey;
            AudioFeedbackService.PlayStopBeep();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _hud.ShowError("Falha no arquivo de áudio");
                return;
            }

            _hud.ShowProcessing();

            // CRITICAL RULE: Save recording to persistent queue BEFORE any network upload attempt!
            var queueItem = PendingAudioRepository.AddRecording(filePath, durationSec, _activeTargetContext);
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
                _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
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
            if (_activeSettingsForm != null && !_activeSettingsForm.IsDisposed)
            {
                if (_activeSettingsForm.WindowState == FormWindowState.Minimized)
                    _activeSettingsForm.WindowState = FormWindowState.Normal;
                _activeSettingsForm.SelectTab(0);
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
            };
            _activeSettingsForm.SelectTab(0);
            _activeSettingsForm.Show();
            _activeSettingsForm.BringToFront();
            _activeSettingsForm.Activate();
        }

        private void OpenPendingForm()
        {
            using (var form = new PendingAudiosForm())
            {
                form.ShowDialog();
                UpdatePendingBadge();
            }
        }

        private void OpenHistoryForm()
        {
            using (var form = new HistoryForm())
            {
                form.ShowDialog();
            }
        }

        private void OnMenuExitClick(object sender, EventArgs e)
        {
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
