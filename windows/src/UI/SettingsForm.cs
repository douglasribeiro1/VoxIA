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
        private CheckBox _chkShowApiKey;

        // Transcription Mode Controls
        private RadioButton _rdoModeParagraphs;
        private RadioButton _rdoModeSingleLine;

        private CheckBox _chkAutoClipboard;
        private CheckBox _chkAutoStart;
        private CheckBox _chkPortableMode;
        private CheckBox _chkAudioBeep;
        private CheckBox _chkShowHud;
        private ComboBox _cboHotkeyChoice;
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

        // Navegação lateral (substitui as abas)
        private readonly List<Button> _navButtons = new List<Button>();
        private readonly List<Panel> _sections = new List<Panel>();
        private int _selectedSection = -1;

        private const int SidebarWidth = 200;
        private const int ContentLeft = 24;
        private const int ContentWidth = 536;

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
            LoadDictionaryList();
        }

        private void InitializeComponent()
        {
            Theme.ApplyForm(this, "Configurações — VoxIA", new Size(SidebarWidth + ContentWidth + ContentLeft * 2, 580));

            Panel header = Theme.CreateHeader("VoxIA  |  Configurações", "Ditado por voz com IA  ·  as alterações são aplicadas ao salvar");
            Panel footer = Theme.CreateFooter();
            footer.Width = this.ClientSize.Width;

            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = SidebarWidth, BackColor = Theme.Surface };
            sidebar.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };

            Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            AddSection(sidebar, content, "Geral", BuildGeneralSection());
            AddSection(sidebar, content, "Ditado e atalhos", BuildDictationSection());
            AddSection(sidebar, content, "Formatação do texto", BuildFormatSection());
            AddSection(sidebar, content, "Dicionário", BuildDictionarySection());

            Label lblVersion = new Label
            {
                Text = "VoxIA para Windows\nGroq Whisper + LLM",
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(20, 360)
            };
            sidebar.Controls.Add(lblVersion);

            // FORM BUTTONS
            _btnCancel = new Button { Text = "Cancelar", Size = new Size(100, 34), DialogResult = DialogResult.Cancel };
            Theme.StyleSecondary(_btnCancel);
            _btnSave = new Button { Text = "Salvar alterações", Size = new Size(150, 34), DialogResult = DialogResult.OK };
            Theme.StylePrimary(_btnSave);
            _btnSave.Click += BtnSave_Click;

            _btnCancel.Location = new Point(this.ClientSize.Width - _btnCancel.Width - 20, 12);
            _btnSave.Location = new Point(_btnCancel.Left - _btnSave.Width - 10, 12);
            _btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Label lblFooterHint = new Label
            {
                Text = "Enter salva  ·  Esc cancela",
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(20, 21)
            };
            footer.Controls.Add(lblFooterHint);
            footer.Controls.Add(_btnSave);
            footer.Controls.Add(_btnCancel);

            // Ordem de adição importa para o Dock: Fill por último
            this.Controls.Add(content);
            this.Controls.Add(sidebar);
            this.Controls.Add(footer);
            this.Controls.Add(header);

            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;

            SelectTab(0);
        }

        private void AddSection(Panel sidebar, Panel content, string title, Panel section)
        {
            int index = _sections.Count;

            Button nav = new Button
            {
                Text = "   " + title,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(0, 16 + index * 42),
                Size = new Size(SidebarWidth - 1, 40),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Theme.FontFamily, 9.75F),
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            nav.FlatAppearance.BorderSize = 0;
            nav.FlatAppearance.MouseOverBackColor = Theme.SurfaceAlt;
            nav.FlatAppearance.MouseDownBackColor = Theme.AccentSoft;
            nav.Click += (s, e) => SelectTab(index);
            nav.Paint += (s, e) =>
            {
                if (_selectedSection == index)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        e.Graphics.FillRectangle(b, 0, 6, 4, nav.Height - 12);
                }
            };

            section.Location = new Point(0, 0);
            section.Dock = DockStyle.Fill;
            section.Visible = false;

            Label lblTitle = new Label { Text = title, Font = Theme.SectionTitle, ForeColor = Theme.Navy, AutoSize = true, Location = new Point(ContentLeft - 2, 16) };
            section.Controls.Add(lblTitle);

            sidebar.Controls.Add(nav);
            content.Controls.Add(section);
            _navButtons.Add(nav);
            _sections.Add(section);
        }

        private Panel CreateSectionPanel()
        {
            return new Panel { BackColor = Theme.Background, AutoScroll = false };
        }

        // ==========================================
        // SEÇÃO 1: GERAL
        // ==========================================
        private Panel BuildGeneralSection()
        {
            Panel section = CreateSectionPanel();

            // Cartão: Chave de API
            Panel cardKey = Theme.CreateCard(new Rectangle(ContentLeft, 52, ContentWidth, 112));
            cardKey.Controls.Add(Theme.CreateCardTitle("Chave de API Groq", new Point(16, 12)));
            _txtApiKey = new TextBox { Location = new Point(16, 38), Width = ContentWidth - 120, UseSystemPasswordChar = true };
            Theme.StyleTextBox(_txtApiKey);
            _chkShowApiKey = new CheckBox { Text = "Mostrar", Location = new Point(ContentWidth - 92, 40), AutoSize = true, ForeColor = Theme.TextMuted };
            _chkShowApiKey.CheckedChanged += (s, e) => _txtApiKey.UseSystemPasswordChar = !_chkShowApiKey.Checked;

            LinkLabel lnkGroq = new LinkLabel
            {
                Text = "Obtenha sua chave gratuita em console.groq.com",
                Location = new Point(16, 74),
                AutoSize = true,
                Font = Theme.Small,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            lnkGroq.LinkArea = new LinkArea(lnkGroq.Text.IndexOf("console"), "console.groq.com".Length);
            lnkGroq.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("https://console.groq.com/keys"); } catch { }
            };
            cardKey.Controls.AddRange(new Control[] { _txtApiKey, _chkShowApiKey, lnkGroq });

            // Cartão: Sistema e comportamento
            Panel cardSys = Theme.CreateCard(new Rectangle(ContentLeft, 176, ContentWidth, 214));
            cardSys.Controls.Add(Theme.CreateCardTitle("Sistema e comportamento", new Point(16, 12)));
            _chkAutoStart = CreateOption("Iniciar automaticamente com o Windows (residente na bandeja)", 42);
            _chkPortableMode = CreateOption("Modo portátil (dados e configurações salvos na pasta ./data/)", 74);
            _chkAutoClipboard = CreateOption("Copiar automaticamente o texto final para a área de transferência", 106);
            _chkAudioBeep = CreateOption("Sinal sonoro discreto ao iniciar e parar a gravação", 138);
            _chkShowHud = CreateOption("Exibir indicador visual flutuante (HUD) durante o ditado", 170);
            _chkAutoClipboard.Checked = true;
            _chkAudioBeep.Checked = true;
            _chkShowHud.Checked = true;
            cardSys.Controls.AddRange(new Control[] { _chkAutoStart, _chkPortableMode, _chkAutoClipboard, _chkAudioBeep, _chkShowHud });

            section.Controls.Add(cardKey);
            section.Controls.Add(cardSys);
            return section;
        }

        private CheckBox CreateOption(string text, int top)
        {
            return new CheckBox { Text = text, Location = new Point(18, top), AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand };
        }

        // ==========================================
        // SEÇÃO 2: DITADO E ATALHOS
        // ==========================================
        private Panel BuildDictationSection()
        {
            Panel section = CreateSectionPanel();

            Panel cardHotkey = Theme.CreateCard(new Rectangle(ContentLeft, 52, ContentWidth, 92));
            cardHotkey.Controls.Add(Theme.CreateCardTitle("Tecla de atalho global para ditado", new Point(16, 12)));
            _cboHotkeyChoice = new ComboBox { Location = new Point(16, 40), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = new Font(Theme.FontFamily, 9.75F) };
            _cboHotkeyChoice.Items.Add("F8 (Recomendado / Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Win (Padrão Clássico)");
            _cboHotkeyChoice.Items.Add("F9 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F10 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F12 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Shift + Espaço");
            _cboHotkeyChoice.SelectedIndex = 0;
            cardHotkey.Controls.Add(_cboHotkeyChoice);
            cardHotkey.Controls.Add(Theme.CreateHint("Funciona em qualquer aplicativo, inclusive no prontuário.", new Point(326, 44), 200));

            Panel cardMode = Theme.CreateCard(new Rectangle(ContentLeft, 156, ContentWidth, 140));
            cardMode.Controls.Add(Theme.CreateCardTitle("Modo de acionamento", new Point(16, 12)));
            _rdoToggleMode = new RadioButton { Text = "Alternar: pressionar uma vez para iniciar e outra para parar e enviar", Location = new Point(18, 40), AutoSize = true, Checked = true, Cursor = Cursors.Hand };
            cardMode.Controls.Add(_rdoToggleMode);
            cardMode.Controls.Add(Theme.CreateHint("Padrão. Ideal para ditados longos.", new Point(36, 62), 0));
            _rdoPushToTalkMode = new RadioButton { Text = "Push-to-talk: manter pressionado para falar e soltar para enviar", Location = new Point(18, 88), AutoSize = true, Cursor = Cursors.Hand };
            cardMode.Controls.Add(_rdoPushToTalkMode);
            cardMode.Controls.Add(Theme.CreateHint("Ideal para frases curtas e rápidas.", new Point(36, 110), 0));

            Panel cardShortcuts = Theme.CreateCard(new Rectangle(ContentLeft, 308, ContentWidth, 82));
            cardShortcuts.Controls.Add(Theme.CreateCardTitle("Outros atalhos", new Point(16, 12)));
            cardShortcuts.Controls.Add(CreateShortcutRow("Ctrl + Shift + W", "Adicionar a palavra selecionada ao dicionário", 38));
            cardShortcuts.Controls.Add(CreateShortcutRow("Esc", "Cancelar a gravação em andamento", 58));

            section.Controls.Add(cardHotkey);
            section.Controls.Add(cardMode);
            section.Controls.Add(cardShortcuts);
            return section;
        }

        private Control CreateShortcutRow(string keys, string description, int top)
        {
            Panel row = new Panel { Location = new Point(16, top), Size = new Size(ContentWidth - 32, 20), BackColor = Color.Transparent };
            Label lblKeys = new Label { Text = keys, Font = Theme.BodyBold, ForeColor = Theme.Navy, Location = new Point(0, 1), Size = new Size(130, 18) };
            Label lblDesc = new Label { Text = description, ForeColor = Theme.Text, Location = new Point(134, 1), AutoSize = true };
            row.Controls.Add(lblKeys);
            row.Controls.Add(lblDesc);
            return row;
        }

        // ==========================================
        // SEÇÃO 3: FORMATAÇÃO DO TEXTO
        // ==========================================
        private Panel BuildFormatSection()
        {
            Panel section = CreateSectionPanel();

            Panel cardMode = Theme.CreateCard(new Rectangle(ContentLeft, 52, ContentWidth, 200));
            cardMode.Controls.Add(Theme.CreateCardTitle("Formatação do texto ditado", new Point(16, 12)));

            _rdoModeParagraphs = new RadioButton
            {
                Text = "Em parágrafos (com quebras de linha)  —  padrão",
                Location = new Point(18, 42),
                AutoSize = true,
                Font = Theme.BodyBold,
                Checked = true,
                Cursor = Cursors.Hand
            };
            Label lblDescParagraphs = Theme.CreateHint(
                "Detecta pausas e organiza o ditado em parágrafos coesos. Recomendado para textos longos, e-mails, prontuários, laudos e documentos em geral.",
                new Point(37, 66), ContentWidth - 60);

            Panel divider = Theme.CreateDivider(new Point(16, 110), ContentWidth - 32);

            _rdoModeSingleLine = new RadioButton
            {
                Text = "Em linha única (sem quebras de linha)",
                Location = new Point(18, 124),
                AutoSize = true,
                Font = Theme.BodyBold,
                Cursor = Cursors.Hand
            };
            Label lblDescSingleLine = Theme.CreateHint(
                "Mantém todo o texto falado em uma única linha contínua. Ideal para campos de pesquisa, formulários web e chats rápidos.",
                new Point(37, 148), ContentWidth - 60);

            cardMode.Controls.AddRange(new Control[] {
                _rdoModeParagraphs, lblDescParagraphs, divider,
                _rdoModeSingleLine, lblDescSingleLine
            });

            Panel cardCompat = Theme.CreateCard(new Rectangle(ContentLeft, 264, ContentWidth, 92));
            cardCompat.Controls.Add(Theme.CreateCardTitle("Compatibilidade com sistemas antigos", new Point(16, 12)));
            cardCompat.Controls.Add(Theme.CreateHint(
                "O texto inserido é convertido automaticamente para caracteres simples: travessões e hífens especiais viram hífen comum (-), aspas curvas viram aspas retas e espaços especiais viram espaço normal. Os acentos são mantidos.",
                new Point(16, 36), ContentWidth - 32));

            section.Controls.Add(cardMode);
            section.Controls.Add(cardCompat);
            return section;
        }

        // ==========================================
        // SEÇÃO 4: DICIONÁRIO DE PALAVRAS
        // ==========================================
        private Panel BuildDictionarySection()
        {
            Panel section = CreateSectionPanel();

            Label lblDictTip = Theme.CreateHint("Dica: selecione uma palavra em qualquer aplicativo e pressione Ctrl + Shift + W.", new Point(ContentLeft, 48), ContentWidth);

            Label lblSearch = new Label { Text = "Buscar", Location = new Point(ContentLeft, 76), AutoSize = true, Font = Theme.BodyBold };
            _txtDictSearch = new TextBox { Location = new Point(ContentLeft + 52, 73), Width = 280 };
            Theme.StyleTextBox(_txtDictSearch);
            _txtDictSearch.TextChanged += (s, e) => FilterDictionaryList();

            _lblDictCount = new Label { Text = "0 palavras", Location = new Point(ContentLeft + 344, 76), AutoSize = true, ForeColor = Theme.TextMuted };

            _lstDictionary = new ListView
            {
                Location = new Point(ContentLeft, 104),
                Size = new Size(ContentWidth, 238)
            };
            Theme.StyleListView(_lstDictionary);
            _lstDictionary.Columns.Add("De (transcrito / erro)", 200);
            _lstDictionary.Columns.Add("Para (substituição correta)", 214);
            _lstDictionary.Columns.Add("Tipo", 100);
            _lstDictionary.DoubleClick += (s, e) => EditSelectedDictionaryEntry();

            _btnAddDict = new Button { Text = "Adicionar", Location = new Point(ContentLeft, 352), Size = new Size(100, 32) };
            Theme.StylePrimary(_btnAddDict);
            _btnAddDict.Click += (s, e) => AddNewDictionaryEntry();

            _btnEditDict = new Button { Text = "Editar", Location = new Point(ContentLeft + 108, 352), Size = new Size(86, 32) };
            Theme.StyleSecondary(_btnEditDict);
            _btnEditDict.Click += (s, e) => EditSelectedDictionaryEntry();

            _btnDeleteDict = new Button { Text = "Excluir", Location = new Point(ContentLeft + 202, 352), Size = new Size(86, 32) };
            Theme.StyleSecondary(_btnDeleteDict);
            _btnDeleteDict.Click += (s, e) => DeleteSelectedDictionaryEntry();

            _btnClearDict = new Button { Text = "Limpar tudo", Location = new Point(ContentLeft + ContentWidth - 110, 352), Size = new Size(110, 32) };
            Theme.StyleDanger(_btnClearDict);
            _btnClearDict.Click += (s, e) => ClearAllDictionaryEntries();

            section.Controls.AddRange(new Control[] {
                lblDictTip, lblSearch, _txtDictSearch, _lblDictCount,
                _lstDictionary, _btnAddDict, _btnEditDict, _btnDeleteDict, _btnClearDict
            });
            return section;
        }

        public void SelectTab(int index)
        {
            if (index < 0 || index >= _sections.Count) return;

            _selectedSection = index;
            for (int i = 0; i < _sections.Count; i++)
            {
                bool active = (i == index);
                _sections[i].Visible = active;
                _navButtons[i].BackColor = active ? Theme.AccentSoft : Theme.Surface;
                _navButtons[i].ForeColor = active ? Theme.Navy : Theme.Text;
                _navButtons[i].Font = new Font(Theme.FontFamily, 9.75F, active ? FontStyle.Bold : FontStyle.Regular);
                _navButtons[i].Invalidate();
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

            Theme.ApplyZebra(_lstDictionary);
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
