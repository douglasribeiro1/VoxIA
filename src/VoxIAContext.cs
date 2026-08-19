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
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _menuRecord;
        private ToolStripMenuItem _menuPrompts;
        private ToolStripMenuItem _menuDict;
        private ToolStripMenuItem _menuPending;
        private ToolStripMenuItem _menuHistory;
        private ToolStripMenuItem _menuShortcuts;
        private ToolStripMenuItem _menuSettings;
        private ToolStripMenuItem _menuApiKey;
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
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();

            _menuRecord = new ToolStripMenuItem("🎙 Iniciar Gravação (Ctrl + Win)", null, OnMenuRecordClick);
            _menuPrompts = new ToolStripMenuItem("🎯 Prompt de IA Ativo");
            _menuDict = new ToolStripMenuItem("📖 Dicionário de Palavras (Ctrl + Shift + W)", null, (s, e) => OpenDictionaryQuickForm());
            _menuPending = new ToolStripMenuItem("📋 Áudios Pendentes", null, (s, e) => OpenPendingForm());
            _menuHistory = new ToolStripMenuItem("📜 Histórico & Estatísticas", null, (s, e) => OpenHistoryForm());
            _menuShortcuts = new ToolStripMenuItem("⌨ Guia de Atalhos...", null, (s, e) => ShowShortcutsDialog());
            _menuSettings = new ToolStripMenuItem("⚙ Configurações", null, (s, e) => OpenSettingsForm());
            _menuApiKey = new ToolStripMenuItem("🔑 Configurar API Key", null, (s, e) => OpenSettingsForm());
            _menuExit = new ToolStripMenuItem("⏻ Sair", null, OnMenuExitClick);

            _trayMenu.Items.Add(_menuRecord);
            _trayMenu.Items.Add(_menuPrompts);
            _trayMenu.Items.Add(_menuDict);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_menuPending);
            _trayMenu.Items.Add(_menuHistory);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_menuShortcuts);
            _trayMenu.Items.Add(_menuSettings);
            _trayMenu.Items.Add(_menuApiKey);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_menuExit);

            _trayMenu.Opening += (s, e) => UpdatePromptsMenu();

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _trayMenu,
                Text = "VoxIA — Ditado por Voz (Ctrl + Win)",
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

            _notifyIcon.DoubleClick += (s, e) => OpenSettingsForm();
            UpdatePendingBadge();
            UpdatePromptsMenu();
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
            var s = SettingsService.Current;
            if (s.PushToTalkMode && _audioRecorder.IsRecording)
            {
                StopAndProcessDictation();
            }
        }

        private void OnCancelPressed()
        {
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
                // 1. Explicitly release modifier keys so target application sees a pure Ctrl + C
                keybd_event(0x10, 0, 0x0002, UIntPtr.Zero); // Shift UP
                keybd_event(0x12, 0, 0x0002, UIntPtr.Zero); // Alt UP
                keybd_event(0x5B, 0, 0x0002, UIntPtr.Zero); // Win UP
                System.Threading.Thread.Sleep(25);

                // 2. Clear clipboard temporarily to detect newly selected content
                ClipboardService.SetText("");
                System.Threading.Thread.Sleep(20);

                // 3. Send clean Ctrl + C keystroke
                keybd_event(0x11, 0, 0, UIntPtr.Zero);        // Ctrl down
                keybd_event(0x43, 0, 0, UIntPtr.Zero);        // C down
                keybd_event(0x43, 0, 0x0002, UIntPtr.Zero); // C up
                keybd_event(0x11, 0, 0x0002, UIntPtr.Zero); // Ctrl up

                System.Threading.Thread.Sleep(80);

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
            string msg = "⌨ GUIA DE ATALHOS GLOBAIS DO VOXIA:\n\n" +
                         "🎙 Iniciar / Parar Ditado:\n" +
                         "   • Atalho Padrão: Ctrl + Win\n" +
                         "   • Push-to-Talk: Manter pressionado para falar\n\n" +
                         "📖 Adicionar Palavra ao Dicionário:\n" +
                         "   • Atalho Rápido: Ctrl + Shift + W (ou Ctrl + Shift + D)\n" +
                         "   • Como usar: Selecione o texto em qualquer app (Word, Chrome, Prontuário) e pressione o atalho.\n\n" +
                         "⏹ Cancelar Gravação Ativa:\n" +
                         "   • Atalho: Esc\n\n" +
                         "⚙ Configurações e Gerenciamento:\n" +
                         "   • Clique duplo no ícone do VoxIA na bandeja do Windows.";

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
                _menuRecord.Text = "⏹ Parar e Enviar (Ctrl + Win)";
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
            _menuRecord.Text = "🎙 Iniciar Gravação (Ctrl + Win)";
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
            _hud.ShowSuccess();
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
                _menuPending.Text = string.Format("📋 Áudios Pendentes ({0})", count);
                _menuPending.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            }
            else
            {
                _menuPending.Text = "📋 Áudios Pendentes";
                _menuPending.Font = new Font(_trayMenu.Font, FontStyle.Regular);
            }
        }

        private void UpdatePromptsMenu()
        {
            if (_menuPrompts == null) return;

            _menuPrompts.DropDownItems.Clear();
            var profiles = SettingsService.Current.PromptProfiles;
            string activeId = SettingsService.Current.ActivePromptId;

            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    var item = new ToolStripMenuItem(profile.Title);
                    string targetId = profile.Id;
                    item.Checked = (profile.Id == activeId);
                    item.Click += (s, e) =>
                    {
                        SettingsService.SetActivePrompt(targetId);
                        UpdatePromptsMenu();
                        ShowNotification("Prompt de IA Selecionado", string.Format("Prompt ativo alterado para: {0}", profile.Title));
                    };
                    _menuPrompts.DropDownItems.Add(item);
                }
            }

            _menuPrompts.DropDownItems.Add(new ToolStripSeparator());
            var manageItem = new ToolStripMenuItem("⚙ Gerenciar Prompts...", null, (s, e) => OpenSettingsForm());
            _menuPrompts.DropDownItems.Add(manageItem);
        }

        private void ShowNotification(string title, string text)
        {
            if (_notifyIcon != null && SettingsService.Current.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
        }

        private void OpenSettingsForm()
        {
            using (var form = new SettingsForm())
            {
                form.ShowDialog();
                UpdatePromptsMenu();
            }
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
