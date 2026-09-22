using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    public class SettingsForm : Form
    {
        private TextBox _txtApiKey;

        // Prompt Profile UI Controls
        private ComboBox _cboPromptProfiles;
        private TextBox _txtPromptContent;
        private Label _lblPromptStatus;
        private Label _lblCharCount;
        private Button _btnNewPrompt;
        private Button _btnRenamePrompt;
        private Button _btnDeletePrompt;
        private Button _btnResetPrompts;
        private List<PromptProfile> _localPrompts = new List<PromptProfile>();
        private string _currentSelectedPromptId = null;
        private bool _isLoadingPrompts = false;

        private CheckBox _chkAutoClipboard;
        private CheckBox _chkAutoStart;
        private CheckBox _chkPortableMode;
        private CheckBox _chkAudioBeep;
        private CheckBox _chkShowHud;
        private ComboBox _cboHotkeyChoice;
        private GroupBox _grpHotkey;
        private RadioButton _rdoToggleMode;
        private RadioButton _rdoPushToTalkMode;

        // Dictionary UI controls
        private ListView _lstDictionary;
        private TextBox _txtDictSearch;
        private Button _btnAddDict;
        private Button _btnEditDict;
        private Button _btnDeleteDict;
        private Button _btnClearDict;
        private Label _lblDictCount;

        private Button _btnSave;
        private Button _btnCancel;

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
            LoadDictionaryList();
        }

        private void InitializeComponent()
        {
            this.Text = "Configurações — VoxIA";
            this.Size = new Size(580, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            TabControl tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(540, 535)
            };

            // ==========================================
            // TAB 1: API & CONEXÃO
            // ==========================================
            TabPage tabApi = new TabPage("Chave API") { BackColor = Color.White };
            
            Label lblKey = new Label { Text = "Groq API Key:", Location = new Point(15, 15), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _txtApiKey = new TextBox { Location = new Point(15, 35), Width = 495, UseSystemPasswordChar = true };
            Label lblKeyHint = new Label { Text = "Obtenha gratuitamente em console.groq.com (armazenada de forma criptografada)", Location = new Point(15, 60), ForeColor = Color.Gray, AutoSize = true };

            Panel pnlApiInfo = new Panel
            {
                Location = new Point(15, 100),
                Size = new Size(495, 120),
                BackColor = Color.FromArgb(240, 246, 255),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label lblApiInfoTitle = new Label
            {
                Text = "⚡ Integração de Ultra-Alta Velocidade",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(10, 10),
                AutoSize = true
            };
            Label lblApiInfoDesc = new Label
            {
                Text = "O VoxIA utiliza os modelos de ponta acelerados por LPU da Groq (Whisper Large v3 para transcrição de alta fidelidade e GPT OSS 120B para pós-processamento ortográfico), proporcionando digitação instantânea e natural em português.",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(33, 37, 41),
                Location = new Point(10, 35),
                Size = new Size(470, 75)
            };
            pnlApiInfo.Controls.Add(lblApiInfoTitle);
            pnlApiInfo.Controls.Add(lblApiInfoDesc);

            tabApi.Controls.AddRange(new Control[] { 
                lblKey, _txtApiKey, lblKeyHint, 
                pnlApiInfo
            });

            // ==========================================
            // TAB 2: PROMPTS DE IA (CUSTOMIZÁVEIS)
            // ==========================================
            TabPage tabPrompts = new TabPage("🤖 Prompts de IA") { BackColor = Color.White };

            Label lblPromptSel = new Label { Text = "Prompt de Inteligência Artificial Ativo:", Location = new Point(15, 12), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _cboPromptProfiles = new ComboBox { Location = new Point(15, 32), Width = 495, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboPromptProfiles.SelectedIndexChanged += CboPromptProfiles_SelectedIndexChanged;

            // Button toolbar for prompt management
            _btnNewPrompt = new Button { Text = "➕ Novo Prompt", Location = new Point(15, 65), Size = new Size(115, 28), BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnNewPrompt.FlatAppearance.BorderSize = 0;
            _btnNewPrompt.Click += BtnNewPrompt_Click;

            _btnRenamePrompt = new Button { Text = "✏ Renomear", Location = new Point(135, 65), Size = new Size(95, 28) };
            _btnRenamePrompt.Click += BtnRenamePrompt_Click;

            _btnDeletePrompt = new Button { Text = "🗑 Excluir", Location = new Point(235, 65), Size = new Size(85, 28) };
            _btnDeletePrompt.Click += BtnDeletePrompt_Click;

            _btnResetPrompts = new Button { Text = "↺ Restaurar Padrões", Location = new Point(355, 65), Size = new Size(155, 28) };
            _btnResetPrompts.Click += BtnResetPrompts_Click;

            Label lblEditor = new Label { Text = "Instruções do Sistema para a Inteligência Artificial (System Prompt):", Location = new Point(15, 102), AutoSize = true, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
            _txtPromptContent = new TextBox
            {
                Location = new Point(15, 122),
                Width = 495,
                Height = 330,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9.25F, FontStyle.Regular)
            };
            _txtPromptContent.TextChanged += TxtPromptContent_TextChanged;

            _lblPromptStatus = new Label { Text = "Prompt Padrão", Location = new Point(15, 460), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5F, FontStyle.Italic) };
            _lblCharCount = new Label { Text = "0 caracteres", Location = new Point(410, 460), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5F) };

            tabPrompts.Controls.AddRange(new Control[] {
                lblPromptSel, _cboPromptProfiles,
                _btnNewPrompt, _btnRenamePrompt, _btnDeletePrompt, _btnResetPrompts,
                lblEditor, _txtPromptContent,
                _lblPromptStatus, _lblCharCount
            });

            // ==========================================
            // TAB 3: COMPORTAMENTO & ATALHOS
            // ==========================================
            TabPage tabBehavior = new TabPage("Comportamento & Atalhos") { BackColor = Color.White };

            // Panel with shortcuts reference
            Panel pnlShortcuts = new Panel
            {
                Location = new Point(15, 12),
                Size = new Size(495, 75),
                BackColor = Color.FromArgb(240, 244, 255),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label lblShortcutsTitle = new Label
            {
                Text = "⌨ GUIA RÁPIDO DE ATALHOS GLOBAIS",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(10, 8),
                AutoSize = true
            };
            Label lblShortcutsInfo = new Label
            {
                Text = "• Ditado por Voz: Tecla F8 (ou atalho configurado abaixo)\n• Dicionário Rápido: Ctrl + Shift + W (ou Ctrl + Shift + D)\n• Cancelar Gravação: Esc",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(33, 37, 41),
                Location = new Point(10, 28),
                AutoSize = true
            };
            pnlShortcuts.Controls.Add(lblShortcutsTitle);
            pnlShortcuts.Controls.Add(lblShortcutsInfo);

            Label lblHotkey = new Label { Text = "Tecla de Atalho Global para Ditado:", Location = new Point(15, 95), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _cboHotkeyChoice = new ComboBox { Location = new Point(15, 115), Width = 495, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboHotkeyChoice.Items.Add("F8 (Recomendado / Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Win (Padrão Clássico)");
            _cboHotkeyChoice.Items.Add("F9 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F10 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F12 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Shift + Espaço");
            _cboHotkeyChoice.SelectedIndex = 0;

            _grpHotkey = new GroupBox { Text = "Modo de Acionamento do Ditado", Location = new Point(15, 150), Size = new Size(495, 80) };
            _rdoToggleMode = new RadioButton { Text = "Pressionar 1x para Iniciar → Pressionar 1x para Parar e Enviar (Padrão)", Location = new Point(15, 22), AutoSize = true, Checked = true };
            _rdoPushToTalkMode = new RadioButton { Text = "Manter pressionado para Falar → Soltar para Parar (Push-to-Talk)", Location = new Point(15, 48), AutoSize = true };
            _grpHotkey.Controls.Add(_rdoToggleMode);
            _grpHotkey.Controls.Add(_rdoPushToTalkMode);

            _chkAutoClipboard = new CheckBox { Text = "Copiar automaticamente o texto final para a Área de Transferência (Clipboard)", Location = new Point(15, 240), Width = 495, AutoSize = true, Checked = true };
            _chkAutoStart = new CheckBox { Text = "Iniciar automaticamente com o Windows (residente na bandeja)", Location = new Point(15, 268), Width = 495, AutoSize = true };
            _chkPortableMode = new CheckBox { Text = "Modo Portátil (Padrão: dados e configurações salvos localmente na pasta ./data/)", Location = new Point(15, 296), Width = 495, AutoSize = true };
            _chkAudioBeep = new CheckBox { Text = "Sinal sonoro discreto ao iniciar e parar gravação (Beep feedback)", Location = new Point(15, 324), Width = 495, AutoSize = true, Checked = true };
            _chkShowHud = new CheckBox { Text = "Exibir indicador visual flutuante (HUD) centralizado na tela", Location = new Point(15, 352), Width = 495, AutoSize = true, Checked = true };

            tabBehavior.Controls.AddRange(new Control[] { pnlShortcuts, lblHotkey, _cboHotkeyChoice, _grpHotkey, _chkAutoClipboard, _chkAutoStart, _chkPortableMode, _chkAudioBeep, _chkShowHud });

            // ==========================================
            // TAB 4: DICIONÁRIO DE PALAVRAS
            // ==========================================
            TabPage tabDict = new TabPage("📖 Dicionário de Palavras") { BackColor = Color.White };

            Panel pnlDictTip = new Panel
            {
                Location = new Point(15, 12),
                Size = new Size(495, 50),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label lblDictTip = new Label
            {
                Text = "Dica: Selecione qualquer palavra em outro app e aperte Ctrl + Shift + W para cadastrar na hora!",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(73, 80, 87),
                Location = new Point(8, 15),
                AutoSize = true
            };
            pnlDictTip.Controls.Add(lblDictTip);

            Label lblSearch = new Label { Text = "🔍 Buscar:", Location = new Point(15, 70), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _txtDictSearch = new TextBox { Location = new Point(80, 67), Width = 280 };
            _txtDictSearch.TextChanged += (s, e) => FilterDictionaryList();

            _lblDictCount = new Label { Text = "0 palavras", Location = new Point(370, 70), AutoSize = true, ForeColor = Color.Gray };

            _lstDictionary = new ListView
            {
                Location = new Point(15, 95),
                Size = new Size(495, 335),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9F)
            };
            _lstDictionary.Columns.Add("De (Transcrito / Erro)", 185);
            _lstDictionary.Columns.Add("Para (Substituição Correta)", 195);
            _lstDictionary.Columns.Add("Tipo", 95);
            _lstDictionary.DoubleClick += (s, e) => EditSelectedDictionaryEntry();

            _btnAddDict = new Button { Text = "➕ Adicionar", Location = new Point(15, 440), Size = new Size(95, 30), BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnAddDict.FlatAppearance.BorderSize = 0;
            _btnAddDict.Click += (s, e) => AddNewDictionaryEntry();

            _btnEditDict = new Button { Text = "✏ Editar", Location = new Point(115, 440), Size = new Size(80, 30) };
            _btnEditDict.Click += (s, e) => EditSelectedDictionaryEntry();

            _btnDeleteDict = new Button { Text = "🗑 Excluir", Location = new Point(200, 440), Size = new Size(80, 30) };
            _btnDeleteDict.Click += (s, e) => DeleteSelectedDictionaryEntry();

            _btnClearDict = new Button { Text = "Limpar Tudo", Location = new Point(410, 440), Size = new Size(100, 30), ForeColor = Color.DarkRed };
            _btnClearDict.Click += (s, e) => ClearAllDictionaryEntries();

            tabDict.Controls.AddRange(new Control[] {
                pnlDictTip, lblSearch, _txtDictSearch, _lblDictCount,
                _lstDictionary, _btnAddDict, _btnEditDict, _btnDeleteDict, _btnClearDict
            });

            tabs.TabPages.Add(tabApi);
            tabs.TabPages.Add(tabPrompts);
            tabs.TabPages.Add(tabBehavior);
            tabs.TabPages.Add(tabDict);

            // FORM BUTTONS
            _btnSave = new Button { Text = "Salvar Alterações", Location = new Point(325, 558), Size = new Size(130, 32), DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += BtnSave_Click;

            _btnCancel = new Button { Text = "Cancelar", Location = new Point(465, 558), Size = new Size(85, 32), DialogResult = DialogResult.Cancel };

            this.Controls.Add(tabs);
            this.Controls.Add(_btnSave);
            this.Controls.Add(_btnCancel);
            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private void LoadSettings()
        {
            var s = SettingsService.Current;
            _txtApiKey.Text = SecureCredentialService.GetApiKey();

            // Load Prompts list
            _localPrompts = new List<PromptProfile>();
            if (s.PromptProfiles != null && s.PromptProfiles.Count > 0)
            {
                foreach (var p in s.PromptProfiles)
                {
                    _localPrompts.Add(new PromptProfile(p.Id, p.Title, p.PromptText, p.IsBuiltIn));
                }
            }
            else
            {
                _localPrompts = SettingsService.GetDefaultProfiles();
            }

            _currentSelectedPromptId = !string.IsNullOrEmpty(s.ActivePromptId) ? s.ActivePromptId : _localPrompts[0].Id;
            RefreshPromptComboBox(_currentSelectedPromptId);

            _chkAutoClipboard.Checked = s.AutoCopyToClipboard;
            _chkAutoStart.Checked = AutoStartService.IsAutoStartEnabled();
            _chkPortableMode.Checked = AppPathService.IsPortableMode;
            _chkAudioBeep.Checked = s.AudioBeepFeedback;
            _chkShowHud.Checked = s.ShowFloatingHud;

            if (s.HotkeyVirtualKey == 0x77) // F8
                _cboHotkeyChoice.SelectedIndex = 0;
            else if (s.HotkeyVirtualKey == 0 || s.HotkeyDisplay == "Ctrl + Win")
                _cboHotkeyChoice.SelectedIndex = 1;
            else if (s.HotkeyVirtualKey == 0x78) // F9
                _cboHotkeyChoice.SelectedIndex = 2;
            else if (s.HotkeyVirtualKey == 0x79) // F10
                _cboHotkeyChoice.SelectedIndex = 3;
            else if (s.HotkeyVirtualKey == 0x7B) // F12
                _cboHotkeyChoice.SelectedIndex = 4;
            else if (s.HotkeyVirtualKey == 0x20) // Ctrl + Shift + Space
                _cboHotkeyChoice.SelectedIndex = 5;
            else
                _cboHotkeyChoice.SelectedIndex = 0;

            if (s.PushToTalkMode)
                _rdoPushToTalkMode.Checked = true;
            else
                _rdoToggleMode.Checked = true;
        }

        private void RefreshPromptComboBox(string selectId = null)
        {
            _isLoadingPrompts = true;
            try
            {
                _cboPromptProfiles.Items.Clear();
                int selectedIndex = 0;

                for (int i = 0; i < _localPrompts.Count; i++)
                {
                    var p = _localPrompts[i];
                    _cboPromptProfiles.Items.Add(p);
                    if (selectId != null && p.Id == selectId)
                    {
                        selectedIndex = i;
                    }
                }

                if (_cboPromptProfiles.Items.Count > 0)
                {
                    _cboPromptProfiles.SelectedIndex = selectedIndex;
                    var selected = _cboPromptProfiles.SelectedItem as PromptProfile;
                    if (selected != null)
                    {
                        _currentSelectedPromptId = selected.Id;
                        var target = _localPrompts.FirstOrDefault(p => p.Id == selected.Id);
                        _txtPromptContent.Text = (target != null && target.PromptText != null) ? target.PromptText : (selected.PromptText ?? "");
                        _lblPromptStatus.Text = selected.IsBuiltIn ? "★ Prompt Padrão de Fábrica (editável)" : "✦ Prompt Customizado pelo Usuário";
                        _btnDeletePrompt.Enabled = !selected.IsBuiltIn && _localPrompts.Count > 1;
                        UpdatePromptCharCount();
                    }
                }
            }
            finally
            {
                _isLoadingPrompts = false;
            }
        }

        private void SaveCurrentPromptBuffer()
        {
            if (!string.IsNullOrEmpty(_currentSelectedPromptId))
            {
                var current = _localPrompts.FirstOrDefault(p => p.Id == _currentSelectedPromptId);
                if (current != null)
                {
                    current.PromptText = _txtPromptContent.Text;
                    current.UpdatedAt = DateTime.UtcNow.ToString("o");
                }
            }
        }

        private void TxtPromptContent_TextChanged(object sender, EventArgs e)
        {
            UpdatePromptCharCount();

            // Real-time synchronization to local profile buffer
            if (!_isLoadingPrompts && !string.IsNullOrEmpty(_currentSelectedPromptId))
            {
                var current = _localPrompts.FirstOrDefault(p => p.Id == _currentSelectedPromptId);
                if (current != null)
                {
                    current.PromptText = _txtPromptContent.Text;
                    current.UpdatedAt = DateTime.UtcNow.ToString("o");
                }
            }
        }

        private void CboPromptProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isLoadingPrompts) return;

            var selected = _cboPromptProfiles.SelectedItem as PromptProfile;
            if (selected == null) return;

            if (selected.Id == _currentSelectedPromptId) return;

            // 1. Save buffer of the previous prompt
            SaveCurrentPromptBuffer();

            // 2. Load newly selected prompt
            _isLoadingPrompts = true;
            try
            {
                _currentSelectedPromptId = selected.Id;
                var target = _localPrompts.FirstOrDefault(p => p.Id == selected.Id);
                _txtPromptContent.Text = (target != null && target.PromptText != null) ? target.PromptText : (selected.PromptText ?? "");
                _lblPromptStatus.Text = selected.IsBuiltIn ? "★ Prompt Padrão de Fábrica (editável)" : "✦ Prompt Customizado pelo Usuário";
                _btnDeletePrompt.Enabled = !selected.IsBuiltIn && _localPrompts.Count > 1;
                UpdatePromptCharCount();
            }
            finally
            {
                _isLoadingPrompts = false;
            }
        }

        private void UpdatePromptCharCount()
        {
            _lblCharCount.Text = string.Format("{0} caracteres", _txtPromptContent.Text.Length);
        }

        private void BtnNewPrompt_Click(object sender, EventArgs e)
        {
            SaveCurrentPromptBuffer();
            string name = PromptInputDialog("Novo Prompt Customizado", "Digite o nome do seu novo Prompt de IA:", "Meu Prompt");
            if (string.IsNullOrWhiteSpace(name)) return;

            var newProfile = new PromptProfile(
                Guid.NewGuid().ToString("N"),
                name.Trim(),
                @"Você é um assistente de digitação por voz e processamento de texto em português.
Sua ÚNICA TAREFA: Retornar o texto ditado devidamente pontuado e formatado conforme as necessidades deste agente.

RETORNE APENAS O TEXTO FINAL.",
                false
            );

            _localPrompts.Add(newProfile);
            _currentSelectedPromptId = newProfile.Id;

            _isLoadingPrompts = true;
            try
            {
                RefreshPromptComboBox(newProfile.Id);
                _txtPromptContent.Text = newProfile.PromptText;
                _lblPromptStatus.Text = "✦ Prompt Customizado pelo Usuário";
                _btnDeletePrompt.Enabled = true;
                UpdatePromptCharCount();
            }
            finally
            {
                _isLoadingPrompts = false;
            }

            _txtPromptContent.Focus();
        }

        private void BtnRenamePrompt_Click(object sender, EventArgs e)
        {
            var selected = _cboPromptProfiles.SelectedItem as PromptProfile;
            if (selected == null) return;

            string newName = PromptInputDialog("Renomear Prompt", "Novo nome para o prompt:", selected.Title);
            if (string.IsNullOrWhiteSpace(newName)) return;

            var target = _localPrompts.FirstOrDefault(p => p.Id == selected.Id);
            if (target != null)
            {
                target.Title = newName.Trim();
                target.UpdatedAt = DateTime.UtcNow.ToString("o");
            }
            selected.Title = newName.Trim();

            _isLoadingPrompts = true;
            try
            {
                RefreshPromptComboBox(selected.Id);
            }
            finally
            {
                _isLoadingPrompts = false;
            }
        }

        private void BtnDeletePrompt_Click(object sender, EventArgs e)
        {
            var selected = _cboPromptProfiles.SelectedItem as PromptProfile;
            if (selected == null || selected.IsBuiltIn || _localPrompts.Count <= 1) return;

            if (MessageBox.Show(string.Format("Tem certeza que deseja excluir o prompt \"{0}\"?", selected.Title), "Excluir Prompt", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _localPrompts.RemoveAll(p => p.Id == selected.Id);
                var next = _localPrompts[0];
                _currentSelectedPromptId = next.Id;

                _isLoadingPrompts = true;
                try
                {
                    RefreshPromptComboBox(next.Id);
                    _txtPromptContent.Text = next.PromptText ?? "";
                    _lblPromptStatus.Text = next.IsBuiltIn ? "★ Prompt Padrão de Fábrica (editável)" : "✦ Prompt Customizado pelo Usuário";
                    _btnDeletePrompt.Enabled = !next.IsBuiltIn && _localPrompts.Count > 1;
                    UpdatePromptCharCount();
                }
                finally
                {
                    _isLoadingPrompts = false;
                }
            }
        }

        private void BtnResetPrompts_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Deseja restaurar os prompts padrão de fábrica? Seus prompts customizados serão preservados.", "Restaurar Padrões", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var defaults = SettingsService.GetDefaultProfiles();
                foreach (var def in defaults)
                {
                    var existing = _localPrompts.FirstOrDefault(p => p.Id == def.Id);
                    if (existing != null)
                    {
                        existing.Title = def.Title;
                        existing.PromptText = def.PromptText;
                        existing.UpdatedAt = DateTime.UtcNow.ToString("o");
                    }
                    else
                    {
                        _localPrompts.Insert(0, def);
                    }
                }

                _currentSelectedPromptId = defaults[0].Id;
                RefreshPromptComboBox(_currentSelectedPromptId);
            }
        }

        private static string PromptInputDialog(string title, string promptText, string defaultValue = "")
        {
            using (Form form = new Form())
            {
                form.Text = title;
                form.Size = new Size(380, 160);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.BackColor = Color.FromArgb(245, 246, 250);

                Label label = new Label() { Left = 16, Top = 16, Text = promptText, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
                TextBox textBox = new TextBox() { Left = 16, Top = 40, Width = 330, Text = defaultValue };
                Button buttonOk = new Button() { Text = "OK", Left = 180, Width = 80, Top = 78, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                Button buttonCancel = new Button() { Text = "Cancelar", Left = 266, Width = 80, Top = 78, DialogResult = DialogResult.Cancel };

                buttonOk.FlatAppearance.BorderSize = 0;
                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(buttonOk);
                form.Controls.Add(buttonCancel);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
            }
        }

        private void LoadDictionaryList(string filter = null)
        {
            _lstDictionary.Items.Clear();
            var entries = DictionaryService.GetEntries();

            int count = 0;
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(filter))
                {
                    bool match = (entry.Original != null && entry.Original.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                              || (entry.Replacement != null && entry.Replacement.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                var item = new ListViewItem(entry.Original ?? "");
                item.SubItems.Add(entry.Replacement ?? "");
                item.SubItems.Add(entry.IsVocabularyOnly ? "Vocabulário" : "Substituição");
                item.Tag = entry.Id;
                _lstDictionary.Items.Add(item);
                count++;
            }

            _lblDictCount.Text = string.Format("{0} palavra(s)", count);
        }

        private void FilterDictionaryList()
        {
            string filter = _txtDictSearch.Text.Trim();
            LoadDictionaryList(filter);
        }

        private void AddNewDictionaryEntry()
        {
            using (var form = new QuickDictionaryForm())
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    LoadDictionaryList(_txtDictSearch.Text.Trim());
                }
            }
        }

        private void EditSelectedDictionaryEntry()
        {
            if (_lstDictionary.SelectedItems.Count == 0) return;
            var item = _lstDictionary.SelectedItems[0];
            string orig = item.Text;
            string repl = item.SubItems.Count > 1 ? item.SubItems[1].Text : orig;

            using (var form = new QuickDictionaryForm(orig))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    LoadDictionaryList(_txtDictSearch.Text.Trim());
                }
            }
        }

        private void DeleteSelectedDictionaryEntry()
        {
            if (_lstDictionary.SelectedItems.Count == 0) return;
            var item = _lstDictionary.SelectedItems[0];
            string id = item.Tag != null ? item.Tag.ToString() : null;

            if (!string.IsNullOrEmpty(id))
            {
                if (MessageBox.Show(string.Format("Deseja realmente remover a palavra \"{0}\" do dicionário?", item.Text), "Remover Termo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    DictionaryService.Remove(id);
                    LoadDictionaryList(_txtDictSearch.Text.Trim());
                }
            }
        }

        private void ClearAllDictionaryEntries()
        {
            if (MessageBox.Show("Tem certeza que deseja apagar TODAS as palavras e regras do dicionário?", "Limpar Dicionário", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                DictionaryService.Clear();
                LoadDictionaryList();
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveCurrentPromptBuffer();

            var s = SettingsService.Current;
            string apiKey = _txtApiKey.Text.Trim();
            SecureCredentialService.SaveApiKey(apiKey);

            s.WhisperModel = "whisper-large-v3";
            s.CorrectionModel = "openai/gpt-oss-120b";

            // Save Prompts List
            s.PromptProfiles = new List<PromptProfile>(_localPrompts);
            var selected = _localPrompts.FirstOrDefault(p => p.Id == _currentSelectedPromptId) ?? (_cboPromptProfiles.SelectedItem as PromptProfile) ?? _localPrompts[0];
            s.ActivePromptId = selected.Id;
            s.SelectedPromptPreset = selected.Title;
            s.CustomPromptText = selected.PromptText;

            s.AutoCopyToClipboard = _chkAutoClipboard.Checked;
            s.AudioBeepFeedback = _chkAudioBeep.Checked;
            s.ShowFloatingHud = _chkShowHud.Checked;
            s.PushToTalkMode = _rdoToggleMode.Checked == false;

            int hotkeyIdx = _cboHotkeyChoice.SelectedIndex;
            if (hotkeyIdx == 0) // F8
            {
                s.HotkeyDisplay = "F8";
                s.HotkeyVirtualKey = 0x77;
                s.HotkeyUseCtrl = false;
                s.HotkeyUseWin = false;
                s.HotkeyUseAlt = false;
                s.HotkeyUseShift = false;
            }
            else if (hotkeyIdx == 1) // Ctrl + Win
            {
                s.HotkeyDisplay = "Ctrl + Win";
                s.HotkeyVirtualKey = 0;
                s.HotkeyUseCtrl = true;
                s.HotkeyUseWin = true;
                s.HotkeyUseAlt = false;
                s.HotkeyUseShift = false;
            }
            else if (hotkeyIdx == 2) // F9
            {
                s.HotkeyDisplay = "F9";
                s.HotkeyVirtualKey = 0x78;
                s.HotkeyUseCtrl = false;
                s.HotkeyUseWin = false;
                s.HotkeyUseAlt = false;
                s.HotkeyUseShift = false;
            }
            else if (hotkeyIdx == 3) // F10
            {
                s.HotkeyDisplay = "F10";
                s.HotkeyVirtualKey = 0x79;
                s.HotkeyUseCtrl = false;
                s.HotkeyUseWin = false;
                s.HotkeyUseAlt = false;
                s.HotkeyUseShift = false;
            }
            else if (hotkeyIdx == 4) // F12
            {
                s.HotkeyDisplay = "F12";
                s.HotkeyVirtualKey = 0x7B;
                s.HotkeyUseCtrl = false;
                s.HotkeyUseWin = false;
                s.HotkeyUseAlt = false;
                s.HotkeyUseShift = false;
            }
            else if (hotkeyIdx == 5) // Ctrl + Shift + Espaço
            {
                s.HotkeyDisplay = "Ctrl + Shift + Espaço";
                s.HotkeyVirtualKey = 0x20;
                s.HotkeyUseCtrl = true;
                s.HotkeyUseWin = false;
                s.HotkeyUseAlt = false;
                s.HotkeyUseShift = true;
            }

            AutoStartService.SetAutoStart(_chkAutoStart.Checked);
            s.AutoStartWithWindows = _chkAutoStart.Checked;

            bool enablePortable = _chkPortableMode.Checked;
            AppPathService.EnablePortableMode(enablePortable);
            s.PortableMode = enablePortable;

            SettingsService.Save();
            this.Close();
        }
    }
}
