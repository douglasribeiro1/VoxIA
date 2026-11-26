using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    public class SettingsForm : Form
    {
        private TextBox _txtApiKey;
        private CheckBox _chkShowApiKey;

        // Seletores: formato do texto (0 = parágrafos, 1 = linha única) e modo de acionamento (0 = alternar, 1 = segurar)
        private SegmentedControl _segFormat;
        private SegmentedControl _segMode;

        private ToggleSwitch _chkAutoClipboard;
        private ToggleSwitch _chkAutoStart;
        private ToggleSwitch _chkPortableMode;
        private ToggleSwitch _chkAudioBeep;
        private ToggleSwitch _chkShowHud;
        private ComboBox _cboHotkeyChoice;
        private ComboBox _cboMicrophone;
        private ToggleSwitch _chkFieldContext;
        private ToggleSwitch _chkBoostMic;
        private ToggleSwitch _chkAutoUpdate;
        private TextBox _txtCustomPrompt;
        private const string CustomPromptExample =
            "Corrija ortografia e pontuação e organize o texto em parágrafos por assunto, como um texto bem escrito em português. Mantenha todas as informações ditadas.";

        /// <summary>Chamado ao clicar em "Procurar atualizações" (definido pelo VoxIAContext).</summary>
        public static Action RequestUpdateCheck;
        private ComboBox _cboHistoryRetention;

        // Opções de "Guardar histórico" e os dias correspondentes (0 = não guardar, -1 = sem limite)
        private static readonly string[] HistoryRetentionLabels = { "Não guardar", "1 dia", "7 dias", "30 dias", "Sem limite" };
        private static readonly int[] HistoryRetentionDays = { 0, 1, 7, 30, -1 };

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
        private readonly List<Control> _navButtons = new List<Control>();
        private readonly List<Panel> _sections = new List<Panel>();
        private int _selectedSection = -1;

        private const int FormWidth = 900;
        private const int FormHeight = 620;
        private const int SidebarWidth = 220;
        private const int PageLeft = 32;
        private const int PageTop = 116;
        private const int PageWidth = 616;
        private const int ColumnWidth = 300;
        private const int ColumnGap = 16;
        private const int CardGap = 16;
        private const string MicrophoneDefaultLabel = "Padrão do sistema";

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
            LoadDictionaryList();
        }

        // Índices das páginas da janela principal
        public const int PageHome = 0;
        public const int PageHistory = 1;
        public const int PageDictionary = 2;
        public const int PagePending = 3;
        public const int PageSettings = 4;
        public const int PageShortcuts = 5;

        private Panel _footer;
        private HomePage _home;

        private void InitializeComponent()
        {
            // Janela principal: abre com o tamanho do protótipo (ou menor, se a tela não couber) e pode ser maximizada
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int clientWidth = Math.Min(1240, Math.Max(1000, workArea.Width - 60));
            int clientHeight = Math.Min(780, Math.Max(560, workArea.Height - 60));
            Theme.ApplyForm(this, "VoxIA", new Size(clientWidth, clientHeight));
            Theme.MakeResizable(this, new Size(1000, 600));

            Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            Panel sidebar = BuildSidebar();
            _footer = Theme.CreateFooter();

            _home = new HomePage();
            _home.NavigateTo = (i) => SelectTab(i);
            AddSection(sidebar, content, "Início", NavIcons.Home, _home);
            AddSection(sidebar, content, "Histórico", NavIcons.History, EmbedForm(new HistoryForm()));
            AddSection(sidebar, content, "Dicionário", NavIcons.Book, BuildDictionarySection());
            AddSection(sidebar, content, "Áudios pendentes", NavIcons.Inbox, EmbedForm(new PendingAudiosForm()));
            AddSection(sidebar, content, "Configurações", NavIcons.Sliders, BuildSettingsSection());
            AddSection(sidebar, content, "Atalhos", NavIcons.Keyboard, BuildShortcutsSection());

            // FORM BUTTONS
            _btnCancel = new Button { Text = "Cancelar", Size = new Size(120, 38) };
            Theme.StyleSecondary(_btnCancel);
            _btnCancel.Font = Theme.BodyBold;
            _btnSave = new Button { Text = "Salvar alterações", Size = new Size(170, 38) };
            Theme.StylePrimary(_btnSave);

            _btnSave.Click += BtnSave_Click;
            // Cancelar desfaz as mudanças não salvas e volta ao Início
            _btnCancel.Click += (s, e) => { LoadSettings(); SelectTab(PageHome); };

            _footer.Height = 58;
            _footer.Width = this.ClientSize.Width;
            Action placeButtons = () =>
            {
                _btnCancel.Location = new Point(_footer.ClientSize.Width - _btnCancel.Width - 24, 10);
                _btnSave.Location = new Point(_btnCancel.Left - _btnSave.Width - 12, 10);
            };
            _footer.Resize += (s, e) => placeButtons();
            placeButtons();
            _footer.Controls.Add(_btnSave);
            _footer.Controls.Add(_btnCancel);

            // Ordem de adição importa para o Dock: Fill por primeiro
            this.Controls.Add(content);
            this.Controls.Add(_footer);
            this.Controls.Add(sidebar);

            this.Activated += (s, e) => { if (_home != null) _home.RefreshData(); };

            SelectTab(PageHome);
        }

        /// <summary>Coloca uma janela existente (Histórico, Pendentes) dentro de uma página da janela principal.</summary>
        private static Panel EmbedForm(Form form)
        {
            Panel host = new Panel { BackColor = Theme.Background };
            int width = form.ClientSize.Width;
            StretchWide(form, width);
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Fill;
            HideCloseButtons(form);
            host.Controls.Add(form);
            form.Visible = true;
            return host;
        }

        /// <summary>Listas e caixas de texto largas acompanham a largura da janela quando ela cresce.</summary>
        private static void StretchWide(Control parent, int width)
        {
            foreach (Control c in parent.Controls)
            {
                if ((c is ListView || c is TextBox) && c.Width > width / 2)
                    c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                if (c.HasChildren) StretchWide(c, width);
            }
        }

        private static void HideCloseButtons(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Button && c.Text == "Fechar") c.Visible = false;
                if (c.HasChildren) HideCloseButtons(c);
            }
        }

        /// <summary>Barra lateral escura com a marca, a navegação e o estado do serviço.</summary>
        private Panel BuildSidebar()
        {
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = Theme.Sidebar };
            sidebar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (Font brand = new Font(Theme.FontFamily, 17F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "VoxIA", brand, new Point(22, 22), Color.White);
                    int w = TextRenderer.MeasureText("VoxIA", brand).Width;
                    TextRenderer.DrawText(e.Graphics, "ditado por voz", Theme.Small, new Point(22 + w, 34), Theme.SidebarMuted);
                }
            };

            Panel status = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Sidebar, Padding = new Padding(14, 0, 14, 18) };
            Theme.RoundPanel card = new Theme.RoundPanel { Dock = DockStyle.Fill, Radius = 12, Fill = Theme.SidebarCard, Stroke = Color.Empty, BackColor = Theme.SidebarCard };
            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush dot = new SolidBrush(Theme.LiveGreen))
                    e.Graphics.FillEllipse(dot, 14, 17, 8, 8);
                TextRenderer.DrawText(e.Graphics, "Pronto para ditar", Theme.BodyBold, new Point(28, 12), Color.FromArgb(233, 231, 224));
                TextRenderer.DrawText(e.Graphics, "Rodando na bandeja, perto do relógio. Pode fechar esta janela.", Theme.Small,
                    new Rectangle(14, 36, card.Width - 28, card.Height - 40), Theme.SidebarMuted, TextFormatFlags.WordBreak);
            };
            status.Controls.Add(card);
            sidebar.Controls.Add(status);

            return sidebar;
        }

        private void AddSection(Panel sidebar, Panel content, string title, string iconPath, Panel section)
        {
            int index = _sections.Count;

            NavButton nav = new NavButton(title, iconPath)
            {
                Location = new Point(14, 78 + index * 48),
                Size = new Size(204, 44)
            };
            nav.Click += (s, e) => SelectTab(index);

            if (!(section is HomePage))
            {
                // Se a janela for baixa demais para a página, o conteúdo rola em vez de ser cortado
                int bottom = 0;
                foreach (Control c in section.Controls)
                    bottom = Math.Max(bottom, c.Bottom);
                section.AutoScroll = true;
                section.AutoScrollMinSize = new Size(0, bottom + 24);
            }

            section.Dock = DockStyle.Fill;
            section.Visible = false;

            sidebar.Controls.Add(nav);
            content.Controls.Add(section);
            _navButtons.Add(nav);
            _sections.Add(section);
        }

        private Panel CreateSectionPanel()
        {
            return new Panel { BackColor = Theme.Background, AutoScroll = false };
        }

        /// <summary>Cabeçalho da página: rótulo pequeno em maiúsculas e título grande em serifa itálica.</summary>
        private static void AddPageHeader(Panel section, string eyebrow, string title)
        {
            section.Controls.Add(Theme.CreateEyebrow(eyebrow, new Point(PageLeft, 40)));
            section.Controls.Add(Theme.CreatePageTitle(title, new Point(PageLeft, 58)));
        }

        /// <summary>Começa um cartão com título; o conteúdo é adicionado depois e a altura é ajustada em EndCard.</summary>
        private static Panel BeginCard(int x, int y, int width, string title)
        {
            Panel card = Theme.CreateCard(new Rectangle(x, y, width, 0));
            if (!string.IsNullOrEmpty(title))
                card.Controls.Add(Theme.CreateCardTitle(title, new Point(16, 14)));
            return card;
        }

        /// <summary>Ícone "?" no canto do título do cartão, com a explicação ao passar o mouse.</summary>
        private static void AddHelp(Panel card, int width, string text)
        {
            card.Controls.Add(Theme.CreateHelpIcon(text, new Point(width - 36, 14)));
        }

        /// <summary>Ajusta a altura do cartão ao conteúdo, adiciona à página e devolve a próxima posição vertical da coluna.</summary>
        private static void EndCard(Panel section, Panel card, ref int columnY)
        {
            int bottom = 0;
            foreach (Control c in card.Controls)
                bottom = Math.Max(bottom, c.Bottom);

            card.Height = bottom + 16;
            section.Controls.Add(card);
            columnY = card.Bottom + CardGap;
        }

        /// <summary>Linha com texto à esquerda e interruptor à direita, dentro de um cartão.</summary>
        private static ToggleSwitch CreateToggleRow(Panel card, string text, int top)
        {
            Label lbl = new Label
            {
                Text = text,
                Font = Theme.Body,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Location = new Point(16, top),
                Size = new Size(ColumnWidth - 16 - 16 - 56, 36),
                TextAlign = ContentAlignment.MiddleLeft
            };
            ToggleSwitch toggle = new ToggleSwitch
            {
                Location = new Point(ColumnWidth - 16 - 44, top + 6),
                BackColor = Theme.Surface
            };
            card.Controls.Add(lbl);
            card.Controls.Add(toggle);
            return toggle;
        }

        /// <summary>Atalho como teclas desenhadas (Ctrl + Shift + W) e uma descrição embaixo.</summary>
        private static void AddShortcutRow(Panel card, string keys, string description, int top)
        {
            int x = 16;
            string[] parts = keys.Split(new[] { " + " }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    card.Controls.Add(new Label
                    {
                        Text = "+",
                        Font = Theme.Small,
                        ForeColor = Theme.TextMuted,
                        BackColor = Color.Transparent,
                        AutoSize = true,
                        Location = new Point(x, top + 4)
                    });
                    x += 18;
                }

                Panel cap = Theme.CreateKeyCap(parts[i], new Point(x, top));
                card.Controls.Add(cap);
                x += cap.Width + 6;
            }

            card.Controls.Add(Theme.CreateHint(description, new Point(16, top + 30), ColumnWidth - 32));
        }

        // ==========================================
        // PÁGINA 1: CONFIGURAÇÕES (duas colunas)
        // ==========================================
        private Panel BuildSettingsSection()
        {
            Panel section = CreateSectionPanel();
            AddPageHeader(section, "Preferências", "Como o VoxIA escuta você.");

            int leftX = PageLeft;
            int rightX = PageLeft + ColumnWidth + ColumnGap;
            int leftY = PageTop;
            int rightY = PageTop;

            // Coluna esquerda: chave e comportamento
            Panel cardKey = BeginCard(leftX, leftY, ColumnWidth, "Chave de API Groq");
            _txtApiKey = new TextBox { Location = new Point(16, 40), Width = ColumnWidth - 32 - 80, UseSystemPasswordChar = true };
            Theme.StyleTextBox(_txtApiKey);
            _chkShowApiKey = new CheckBox
            {
                Text = "Mostrar",
                Location = new Point(ColumnWidth - 16 - 66, 42),
                AutoSize = true,
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface,
                Cursor = Cursors.Hand
            };
            _chkShowApiKey.CheckedChanged += (s, e) => _txtApiKey.UseSystemPasswordChar = !_chkShowApiKey.Checked;

            Label lblKeySteps = Theme.CreateHelpIcon(
                "Não tem chave? É grátis e leva 2 minutos:\n" +
                "1. Clique em \"Abrir site da Groq\" e entre com sua conta Google (ou e-mail).\n" +
                "2. Clique em \"Create API Key\", dê um nome (ex.: VoxIA) e copie a chave (começa com gsk_).\n" +
                "3. Cole a chave no campo acima e clique em Salvar alterações.",
                new Point(ColumnWidth - 36, 14));

            Button btnOpenGroq = new Button
            {
                Text = "Abrir site da Groq",
                Location = new Point(16, 76),
                Size = new Size(170, 32),
                Cursor = Cursors.Hand
            };
            Theme.StyleSecondary(btnOpenGroq);
            btnOpenGroq.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("https://console.groq.com/keys"); } catch { }
            };
            cardKey.Controls.AddRange(new Control[] { _txtApiKey, _chkShowApiKey, lblKeySteps, btnOpenGroq });
            EndCard(section, cardKey, ref leftY);

            Panel cardSys = BeginCard(leftX, leftY, ColumnWidth, "Sistema e comportamento");
            _chkAutoStart = CreateToggleRow(cardSys, "Iniciar com o Windows (na bandeja)", 40);
            _chkPortableMode = CreateToggleRow(cardSys, "Modo portátil (dados na pasta ./data/)", 84);
            _chkAutoClipboard = CreateToggleRow(cardSys, "Copiar o texto final automaticamente", 128);
            _chkAudioBeep = CreateToggleRow(cardSys, "Sinal sonoro ao iniciar e parar", 172);
            _chkShowHud = CreateToggleRow(cardSys, "Indicador flutuante (HUD) durante o ditado", 216);
            _chkBoostMic = CreateToggleRow(cardSys, "Microfone no máximo durante o ditado (baixa sozinho se saturar)", 260);
            _chkBoostMic.Checked = true;
            _chkAutoUpdate = CreateToggleRow(cardSys, "Procurar atualizações automaticamente", 304);
            _chkAutoUpdate.Checked = true;
            _chkAutoClipboard.Checked = true;
            _chkAudioBeep.Checked = true;
            _chkShowHud.Checked = true;
            EndCard(section, cardSys, ref leftY);

            Panel cardPrivacy = BeginCard(leftX, leftY, ColumnWidth, "Privacidade");
            _chkFieldContext = CreateToggleRow(cardPrivacy, "Enviar o texto já escrito no campo", 40);
            AddHelp(cardPrivacy, ColumnWidth,
                "Enviar o texto já escrito no campo: ajuda a pontuar a continuação, mas manda à Groq parte do que já está escrito (que pode ter dados pessoais). Desligado por padrão.\n\n" +
                "O histórico e a chave ficam protegidos pelo Windows e só abrem neste computador, com o seu usuário. Os logs não guardam o texto ditado e são apagados após 7 dias.");
            int historyTop = 92;
            Label lblHistory = new Label
            {
                Text = "Guardar histórico",
                Font = Theme.Body,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Location = new Point(16, historyTop),
                Size = new Size(130, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _cboHistoryRetention = new ComboBox
            {
                Location = new Point(ColumnWidth - 16 - 130, historyTop + 2),
                Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                Font = new Font(Theme.FontFamily, 9.75F)
            };
            _cboHistoryRetention.Items.AddRange(HistoryRetentionLabels);
            _cboHistoryRetention.SelectedIndex = 2;
            cardPrivacy.Controls.Add(lblHistory);
            cardPrivacy.Controls.Add(_cboHistoryRetention);
            EndCard(section, cardPrivacy, ref leftY);

            // Coluna direita: texto e compatibilidade
            Panel cardFormat = BeginCard(rightX, rightY, ColumnWidth, "Idioma e texto");
            _segFormat = new SegmentedControl { Location = new Point(16, 40), Size = new Size(ColumnWidth - 32, 36) };
            _segFormat.SetItems("Parágrafos", "Linha única", "Personalizado");
            cardFormat.Controls.Add(_segFormat);
            AddHelp(cardFormat, ColumnWidth,
                "Parágrafos: a IA organiza o texto em parágrafos por assunto.\n" +
                "Linha única: tudo numa linha só, bom para buscas e chats.\n" +
                "Personalizado: você escreve como a IA deve corrigir e formatar o texto (ex.: \"use tópicos\", \"escreva de forma formal\"). A IA continua proibida de responder perguntas ou inventar conteúdo.");
            _txtCustomPrompt = new TextBox
            {
                Location = new Point(16, 86),
                Size = new Size(ColumnWidth - 32, 96),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(Theme.FontFamily, 9.5F)
            };
            cardFormat.Controls.Add(_txtCustomPrompt);
            Action syncCustom = () =>
            {
                bool custom = _segFormat.SelectedIndex == 2;
                _txtCustomPrompt.Enabled = custom;
                if (custom && string.IsNullOrWhiteSpace(_txtCustomPrompt.Text)) _txtCustomPrompt.Text = CustomPromptExample;
            };
            _segFormat.UserSelectionChanged += (s, e) => syncCustom();
            EndCard(section, cardFormat, ref rightY);

            Panel cardMic = BeginCard(rightX, rightY, ColumnWidth, "Microfone");
            _cboMicrophone = new ComboBox
            {
                Location = new Point(16, 40),
                Width = ColumnWidth - 32,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                Font = new Font(Theme.FontFamily, 9.75F)
            };
            _cboMicrophone.Items.Add(MicrophoneDefaultLabel);
            foreach (string name in MicrophoneService.GetInputDeviceNames())
            {
                if (!string.IsNullOrEmpty(name)) _cboMicrophone.Items.Add(name);
            }
            _cboMicrophone.SelectedIndex = 0;
            cardMic.Controls.Add(_cboMicrophone);
            AddHelp(cardMic, ColumnWidth, "Microfone usado para ditar. \"Padrão do sistema\" segue o que o Windows estiver usando.");
            EndCard(section, cardMic, ref rightY);

            Panel cardCompat = BeginCard(rightX, rightY, ColumnWidth, "Compatibilidade");
            cardCompat.Controls.Add(Theme.CreateHint("Texto compatível com qualquer programa.", new Point(16, 44), ColumnWidth - 72));
            AddHelp(cardCompat, ColumnWidth, "Travessões e hífens especiais viram hífen comum (-), aspas curvas viram aspas retas e espaços especiais viram espaço normal. Os acentos são mantidos.");
            EndCard(section, cardCompat, ref rightY);

            // Versão instalada e busca de atualizações
            Panel cardAbout = BeginCard(rightX, rightY, ColumnWidth, "Versão do VoxIA");
            string versionText = BuildInfo.Build > 0
                ? string.Format("Você está na versão build {0}.", BuildInfo.Build)
                : "Versão compilada à mão (sem atualização automática).";
            cardAbout.Controls.Add(new Label { Text = versionText, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = true, Location = new Point(16, 44) });
            Button btnCheckUpdate = new Button { Text = "Procurar atualizações", Location = new Point(16, 74), Size = new Size(190, 32), Cursor = Cursors.Hand };
            Theme.StyleSecondary(btnCheckUpdate);
            btnCheckUpdate.Click += (s, e) => { if (RequestUpdateCheck != null) RequestUpdateCheck(); };
            cardAbout.Controls.Add(btnCheckUpdate);
            AddHelp(cardAbout, ColumnWidth, "O VoxIA procura versões novas sozinho (ao abrir e uma vez por dia) e avisa perto do relógio. A opção fica em Sistema e comportamento.");
            EndCard(section, cardAbout, ref rightY);

            return section;
        }

        // ==========================================
        // PÁGINA 2: ATALHOS (duas colunas)
        // ==========================================
        private Panel BuildShortcutsSection()
        {
            Panel section = CreateSectionPanel();
            AddPageHeader(section, "Atalhos", "Teclas que dão o comando.");

            int leftX = PageLeft;
            int rightX = PageLeft + ColumnWidth + ColumnGap;
            int leftY = PageTop;
            int rightY = PageTop;

            Panel cardHotkey = BeginCard(leftX, leftY, ColumnWidth, "Tecla de atalho global");
            _cboHotkeyChoice = new ComboBox
            {
                Location = new Point(16, 40),
                Width = ColumnWidth - 32,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                Font = new Font(Theme.FontFamily, 9.75F)
            };
            _cboHotkeyChoice.Items.Add("F8 (Recomendado / Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Win (Padrão Clássico)");
            _cboHotkeyChoice.Items.Add("F9 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F10 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("F12 (Tecla Única)");
            _cboHotkeyChoice.Items.Add("Ctrl + Shift + Espaço");
            _cboHotkeyChoice.SelectedIndex = 0;
            cardHotkey.Controls.Add(_cboHotkeyChoice);
            AddHelp(cardHotkey, ColumnWidth, "Funciona em qualquer aplicativo: clique onde quer escrever, aperte o atalho e fale.");
            EndCard(section, cardHotkey, ref leftY);

            Panel cardMode = BeginCard(leftX, leftY, ColumnWidth, "Modo de acionamento");
            _segMode = new SegmentedControl { Location = new Point(16, 40), Size = new Size(ColumnWidth - 32, 36) };
            _segMode.SetItems("Alternar", "Segurar");
            cardMode.Controls.Add(_segMode);
            AddHelp(cardMode, ColumnWidth, "Alternar: uma vez inicia e outra para e envia. Segurar: fale enquanto mantém pressionado e solte para enviar.");
            EndCard(section, cardMode, ref leftY);

            Panel cardShortcuts = BeginCard(rightX, rightY, ColumnWidth, "Outros atalhos");
            AddShortcutRow(cardShortcuts, "Ctrl + Shift + W", "Adiciona a palavra selecionada ao dicionário", 40);
            AddShortcutRow(cardShortcuts, "Esc", "Cancela a gravação em andamento", 104);
            EndCard(section, cardShortcuts, ref rightY);

            return section;
        }

        // ==========================================
        // PÁGINA 3: VOCABULÁRIO (cartão largo)
        // ==========================================
        private Panel BuildDictionarySection()
        {
            Panel section = CreateSectionPanel();
            AddPageHeader(section, "Vocabulário", "Palavras que o VoxIA aprende.");

            Panel card = BeginCard(PageLeft, PageTop, PageWidth, "Dicionário");
            AddHelp(card, PageWidth, "Ensine palavras que o VoxIA erra (nomes, termos técnicos). Dica: selecione uma palavra em qualquer aplicativo e pressione Ctrl + Shift + W.");

            Label lblSearch = new Label { Text = "Buscar", Location = new Point(16, 86), AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.BodyBold };
            _txtDictSearch = new TextBox { Location = new Point(70, 82), Width = 260 };
            Theme.StyleTextBox(_txtDictSearch);
            _txtDictSearch.TextChanged += (s, e) => FilterDictionaryList();

            _lblDictCount = new Label { Text = "0 palavras", Location = new Point(344, 86), AutoSize = true, ForeColor = Theme.TextMuted, BackColor = Color.Transparent };

            _lstDictionary = new ListView
            {
                Location = new Point(16, 118),
                Size = new Size(PageWidth - 32, 230)
            };
            Theme.StyleListView(_lstDictionary);
            _lstDictionary.Columns.Add("De (transcrito / erro)", 200);
            _lstDictionary.Columns.Add("Para (substituição correta)", 254);
            _lstDictionary.Columns.Add("Tipo", 120);
            _lstDictionary.DoubleClick += (s, e) => EditSelectedDictionaryEntry();

            int buttonsTop = 362;

            _btnAddDict = new Button { Text = "Adicionar", Location = new Point(16, buttonsTop), Size = new Size(100, 32) };
            Theme.StylePrimary(_btnAddDict);
            _btnAddDict.Click += (s, e) => AddNewDictionaryEntry();

            _btnEditDict = new Button { Text = "Editar", Location = new Point(124, buttonsTop), Size = new Size(86, 32) };
            Theme.StyleSecondary(_btnEditDict);
            _btnEditDict.Click += (s, e) => EditSelectedDictionaryEntry();

            _btnDeleteDict = new Button { Text = "Excluir", Location = new Point(218, buttonsTop), Size = new Size(86, 32) };
            Theme.StyleSecondary(_btnDeleteDict);
            _btnDeleteDict.Click += (s, e) => DeleteSelectedDictionaryEntry();

            _btnClearDict = new Button { Text = "Limpar tudo", Location = new Point(PageWidth - 16 - 110, buttonsTop), Size = new Size(110, 32) };
            Theme.StyleDanger(_btnClearDict);
            _btnClearDict.Click += (s, e) => ClearAllDictionaryEntries();

            card.Controls.AddRange(new Control[] {
                lblSearch, _txtDictSearch, _lblDictCount,
                _lstDictionary, _btnAddDict, _btnEditDict, _btnDeleteDict, _btnClearDict
            });

            int y = PageTop;
            EndCard(section, card, ref y);
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
                ((NavButton)_navButtons[i]).Active = active;
            }
            // Salvar / Cancelar só fazem sentido na página de Configurações
            if (_footer != null) _footer.Visible = (index == PageSettings);
            if (index == PageHome && _home != null) _home.RefreshData();
        }

        private void LoadSettings()
        {
            var s = SettingsService.Current;
            _txtApiKey.Text = SecureCredentialService.GetApiKey();

            _segFormat.SelectedIndex = s.ActivePromptId == "default_single_line" ? 1 : (s.ActivePromptId == SettingsService.CustomPromptId ? 2 : 0);
            _txtCustomPrompt.Text = s.CustomPromptText ?? "";
            _txtCustomPrompt.Enabled = _segFormat.SelectedIndex == 2;

            _chkAutoClipboard.Checked = s.AutoCopyToClipboard;
            _chkAutoStart.Checked = AutoStartService.IsAutoStartEnabled();
            _chkPortableMode.Checked = AppPathService.IsPortableMode;
            _chkAudioBeep.Checked = s.AudioBeepFeedback;
            _chkShowHud.Checked = s.ShowFloatingHud;
            _chkFieldContext.Checked = s.EnableFieldContext;
            _chkBoostMic.Checked = s.BoostMicrophoneDuringDictation;
            _chkAutoUpdate.Checked = s.AutoCheckUpdates;
            int retentionIndex = Array.IndexOf(HistoryRetentionDays, s.HistoryRetentionDays);
            _cboHistoryRetention.SelectedIndex = retentionIndex >= 0 ? retentionIndex : 2;

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

            _segMode.SelectedIndex = s.PushToTalkMode ? 1 : 0;

            _cboMicrophone.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(s.PreferredMicrophone))
            {
                int micIndex = _cboMicrophone.Items.IndexOf(s.PreferredMicrophone);
                if (micIndex > 0) _cboMicrophone.SelectedIndex = micIndex;
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

            s.WhisperModel = "whisper-large-v3";
            s.CorrectionModel = "openai/gpt-oss-120b";

            string customText = (_txtCustomPrompt.Text ?? "").Trim();
            if (_segFormat.SelectedIndex == 2 && customText.Length == 0)
            {
                MessageBox.Show("Escreva o seu prompt no modo Personalizado (ou escolha Parágrafos ou Linha única).", "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            s.PromptProfiles = SettingsService.GetDefaultProfiles();
            s.CustomPromptText = customText;
            if (customText.Length > 0)
            {
                s.PromptProfiles.Add(new PromptProfile(SettingsService.CustomPromptId, "Personalizado", customText));
            }
            if (_segFormat.SelectedIndex == 1)
            {
                s.ActivePromptId = "default_single_line";
                s.SelectedPromptPreset = "Transcrever em Linha Única";
            }
            else if (_segFormat.SelectedIndex == 2)
            {
                s.ActivePromptId = SettingsService.CustomPromptId;
                s.SelectedPromptPreset = "Personalizado";
            }
            else
            {
                s.ActivePromptId = "default_paragraphs";
                s.SelectedPromptPreset = "Transcrever em Parágrafos";
            }

            s.AutoCopyToClipboard = _chkAutoClipboard.Checked;
            s.AudioBeepFeedback = _chkAudioBeep.Checked;
            s.ShowFloatingHud = _chkShowHud.Checked;
            s.EnableFieldContext = _chkFieldContext.Checked;
            s.BoostMicrophoneDuringDictation = _chkBoostMic.Checked;
            s.AutoCheckUpdates = _chkAutoUpdate.Checked;
            s.HistoryRetentionDays = HistoryRetentionDays[Math.Max(0, _cboHistoryRetention.SelectedIndex)];
            s.PushToTalkMode = _segMode.SelectedIndex == 1;
            s.PreferredMicrophone = _cboMicrophone.SelectedIndex > 0 ? (string)_cboMicrophone.SelectedItem : "";

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

            // Depois de trocar o modo portátil, para a chave ser gravada na pasta de dados que vale agora
            SecureCredentialService.SaveApiKey(apiKey);
            SettingsService.Save();
            HistoryRepository.ApplyRetention();
            SelectTab(PageHome);
        }
    }
}
