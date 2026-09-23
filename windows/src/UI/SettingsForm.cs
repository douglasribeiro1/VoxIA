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

        // Transcription Mode Controls
        private RadioButton _rdoModeParagraphs;
        private RadioButton _rdoModeSingleLine;

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
        private TabControl _tabs;

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
            LoadDictionaryList();
        }

        private void InitializeComponent()
        {
            this.Text = "Configurações — VoxIA";
            this.Size = new Size(580, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            _tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(540, 480)
            };

            // ==========================================
            // TAB 1: CONFIGURAÇÕES GERAIS (TELA CENTRAL)
            // ==========================================
            TabPage tabConfig = new TabPage("⚙ Configurações") { BackColor = Color.White };
            
            // 1. Chave de API Groq
            Label lblKey = new Label { Text = "🔑 Chave de API Groq:", Location = new Point(15, 12), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _txtApiKey = new TextBox { Location = new Point(15, 33), Width = 495, Font = new Font("Segoe UI", 9F) };
            Label lblKeyHint = new Label { Text = "Obtenha sua chave gratuita em console.groq.com", Location = new Point(15, 58), ForeColor = Color.Gray, AutoSize = true, Font = new Font("Segoe UI", 8.25F) };

            // 2. Tecla de Atalho Global
            Label lblHotkey = new Label { Text = "⌨ Tecla de Atalho Global para Ditado:", Location = new Point(15, 86), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _cboHotkeyChoice = new ComboBox { Location = new Point(15, 107), Width = 495, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboHotkeyChoice.Items.Add("F8 (Recomendado / Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Win (Padrão Clássico)");
            _cboHotkeyChoice.Items.Add("F9 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F10 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F12 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Shift + Espaço");
            _cboHotkeyChoice.SelectedIndex = 0;

            // 3. Modo de Acionamento
            _grpHotkey = new GroupBox { Text = "Modo de Acionamento do Ditado", Location = new Point(15, 138), Size = new Size(495, 74) };
            _rdoToggleMode = new RadioButton { Text = "Pressionar 1x para Iniciar → Pressionar 1x para Parar e Enviar (Padrão)", Location = new Point(15, 20), AutoSize = true, Checked = true };
            _rdoPushToTalkMode = new RadioButton { Text = "Manter pressionado para Falar → Soltar para Parar (Push-to-Talk)", Location = new Point(15, 45), AutoSize = true };
            _grpHotkey.Controls.Add(_rdoToggleMode);
            _grpHotkey.Controls.Add(_rdoPushToTalkMode);

            // 4. Opções do Sistema e Comportamento
            GroupBox grpOptions = new GroupBox { Text = "Opções do Sistema e Comportamento", Location = new Point(15, 220), Size = new Size(495, 166) };
            _chkAutoStart = new CheckBox { Text = "Iniciar automaticamente com o Windows (residente na bandeja)", Location = new Point(15, 22), Width = 465, AutoSize = true };
            _chkPortableMode = new CheckBox { Text = "Modo Portátil (Padrão: dados e configurações salvos localmente na pasta ./data/)", Location = new Point(15, 48), Width = 465, AutoSize = true };
            _chkAutoClipboard = new CheckBox { Text = "Copiar automaticamente o texto final para a Área de Transferência (Clipboard)", Location = new Point(15, 74), Width = 465, AutoSize = true, Checked = true };
            _chkAudioBeep = new CheckBox { Text = "Sinal sonoro discreto ao iniciar e parar gravação (Beep feedback)", Location = new Point(15, 100), Width = 465, AutoSize = true, Checked = true };
            _chkShowHud = new CheckBox { Text = "Exibir indicador visual flutuante (HUD) centralizado na tela", Location = new Point(15, 126), Width = 465, AutoSize = true, Checked = true };
            grpOptions.Controls.AddRange(new Control[] { _chkAutoStart, _chkPortableMode, _chkAutoClipboard, _chkAudioBeep, _chkShowHud });

            tabConfig.Controls.AddRange(new Control[] {
                lblKey, _txtApiKey, lblKeyHint,
                lblHotkey, _cboHotkeyChoice, _grpHotkey,
                grpOptions
            });

            // ==========================================
            // TAB 2: MODO DE TRANSCRIÇÃO
            // ==========================================
            TabPage tabFormat = new TabPage("📝 Modo de Transcrição") { BackColor = Color.White };

            GroupBox grpMode = new GroupBox
            {
                Text = "Formatação do Texto Ditado",
                Location = new Point(15, 15),
                Size = new Size(495, 230),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _rdoModeParagraphs = new RadioButton
            {
                Text = "¶  Transcrever em Parágrafos (com quebras de linha) [Padrão]",
                Location = new Point(20, 30),
                Size = new Size(455, 24),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Checked = true
            };
            Label lblDescParagraphs = new Label
            {
                Text = "Detecta pausas e organiza o ditado em parágrafos coesos e bem estruturados. Recomendado para textos longos, e-mails, prontuários, laudos e documentos em geral.",
                Location = new Point(42, 56),
                Size = new Size(430, 48),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            _rdoModeSingleLine = new RadioButton
            {
                Text = "─  Transcrever em Linha Única (sem quebras de linha)",
                Location = new Point(20, 115),
                Size = new Size(455, 24),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            Label lblDescSingleLine = new Label
            {
                Text = "Mantém todo o texto falado em uma única linha contínua, sem quebras de linha ou múltiplos parágrafos. Ideal para campos de pesquisa, formulários web e chats rápidos.",
                Location = new Point(42, 141),
                Size = new Size(430, 48),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            grpMode.Controls.AddRange(new Control[] {
                _rdoModeParagraphs, lblDescParagraphs,
                _rdoModeSingleLine, lblDescSingleLine
            });

            tabFormat.Controls.AddRange(new Control[] { grpMode });

            // ==========================================
            // TAB 3: DICIONÁRIO DE PALAVRAS
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
                Size = new Size(495, 280),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9F)
            };
            _lstDictionary.Columns.Add("De (Transcrito / Erro)", 185);
            _lstDictionary.Columns.Add("Para (Substituição Correta)", 195);
            _lstDictionary.Columns.Add("Tipo", 95);
            _lstDictionary.DoubleClick += (s, e) => EditSelectedDictionaryEntry();

            _btnAddDict = new Button { Text = "➕ Adicionar", Location = new Point(15, 385), Size = new Size(95, 30), BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnAddDict.FlatAppearance.BorderSize = 0;
            _btnAddDict.Click += (s, e) => AddNewDictionaryEntry();

            _btnEditDict = new Button { Text = "✏ Editar", Location = new Point(115, 385), Size = new Size(80, 30) };
            _btnEditDict.Click += (s, e) => EditSelectedDictionaryEntry();

            _btnDeleteDict = new Button { Text = "🗑 Excluir", Location = new Point(200, 385), Size = new Size(80, 30) };
            _btnDeleteDict.Click += (s, e) => DeleteSelectedDictionaryEntry();

            _btnClearDict = new Button { Text = "Limpar Tudo", Location = new Point(410, 385), Size = new Size(100, 30), ForeColor = Color.DarkRed };
            _btnClearDict.Click += (s, e) => ClearAllDictionaryEntries();

            tabDict.Controls.AddRange(new Control[] {
                pnlDictTip, lblSearch, _txtDictSearch, _lblDictCount,
                _lstDictionary, _btnAddDict, _btnEditDict, _btnDeleteDict, _btnClearDict
            });

            _tabs.TabPages.Add(tabConfig);
            _tabs.TabPages.Add(tabFormat);
            _tabs.TabPages.Add(tabDict);
            _tabs.SelectedIndex = 0;

            // FORM BUTTONS
            _btnSave = new Button { Text = "Salvar Alterações", Location = new Point(325, 502), Size = new Size(130, 32), DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += BtnSave_Click;

            _btnCancel = new Button { Text = "Cancelar", Location = new Point(465, 502), Size = new Size(85, 32), DialogResult = DialogResult.Cancel };

            this.Controls.Add(_tabs);
            this.Controls.Add(_btnSave);
            this.Controls.Add(_btnCancel);
            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        public void SelectTab(int index)
        {
            if (_tabs != null && index >= 0 && index < _tabs.TabPages.Count)
            {
                _tabs.SelectedIndex = index;
            }
        }

        private void LoadSettings()
        {
            var s = SettingsService.Current;
            _txtApiKey.Text = SecureCredentialService.GetApiKey();

            if (s.ActivePromptId == "default_single_line")
                _rdoModeSingleLine.Checked = true;
            else
                _rdoModeParagraphs.Checked = true;

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
            var s = SettingsService.Current;
            string apiKey = _txtApiKey.Text.Trim();
            SecureCredentialService.SaveApiKey(apiKey);

            s.WhisperModel = "whisper-large-v3";
            s.CorrectionModel = "openai/gpt-oss-120b";

            if (_rdoModeSingleLine.Checked)
            {
                s.ActivePromptId = "default_single_line";
                s.SelectedPromptPreset = "Transcrever em Linha Única";
            }
            else
            {
                s.ActivePromptId = "default_paragraphs";
                s.SelectedPromptPreset = "Transcrever em Parágrafos";
            }
            s.PromptProfiles = SettingsService.GetDefaultProfiles();
            s.CustomPromptText = "";

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
