using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    public class HistoryForm : Form
    {
        private TextBox _txtSearch;
        private ListView _listView;
        private TextBox _txtPreview;
        private Label _lblStatCount;
        private Label _lblStatWords;
        private Label _lblStatMinutes;
        private Button _btnCopy;
        private Button _btnClear;
        private Button _btnClose;

        public HistoryForm()
        {
            InitializeComponent();
            RefreshHistory();
        }

        private void InitializeComponent()
        {
            const int W = 780;
            const int M = 22;
            Theme.ApplyForm(this, "Histórico de Transcrições — VoxIA", new Size(W, 600));

            Panel header = Theme.CreateHeader("Histórico de transcrições", "Consulte, copie e reaproveite os textos ditados");
            Panel footer = Theme.CreateFooter();
            footer.Width = W;

            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            // Indicadores
            int statW = (W - M * 2 - 24) / 3;
            _lblStatCount = AddStatCard(body, "Ditados", new Rectangle(M, 16, statW, 64));
            _lblStatWords = AddStatCard(body, "Palavras transcritas", new Rectangle(M + statW + 12, 16, statW, 64));
            _lblStatMinutes = AddStatCard(body, "Minutos economizados (estimativa)", new Rectangle(M + (statW + 12) * 2, 16, statW, 64));

            Label lblList = new Label { Text = "Transcrições recentes", Font = Theme.CardTitle, ForeColor = Theme.Navy, Location = new Point(M, 96), AutoSize = true };
            Label lblSearch = new Label { Text = "Buscar", Location = new Point(W - M - 260, 97), AutoSize = true, Font = Theme.BodyBold };
            _txtSearch = new TextBox { Location = new Point(W - M - 208, 94), Width = 208 };
            Theme.StyleTextBox(_txtSearch);
            _txtSearch.TextChanged += (s, e) => RefreshHistory();

            _listView = new ListView
            {
                Location = new Point(M, 124),
                Size = new Size(W - M * 2, 180),
                MultiSelect = false
            };
            Theme.StyleListView(_listView);

            _listView.Columns.Add("Data / hora", 130);
            _listView.Columns.Add("Aplicativo", 130);
            _listView.Columns.Add("Duração", 70);
            _listView.Columns.Add("Palavras", 70);
            _listView.Columns.Add("Texto transcrito", 330);

            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            Label lblPreview = new Label { Text = "Texto final corrigido", Font = Theme.CardTitle, ForeColor = Theme.Navy, Location = new Point(M, 316), AutoSize = true };
            _txtPreview = new TextBox
            {
                Location = new Point(M, 340),
                Size = new Size(W - M * 2, 118),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            Theme.StyleTextBox(_txtPreview);
            _txtPreview.BackColor = Theme.Surface;

            body.Controls.AddRange(new Control[] { lblList, lblSearch, _txtSearch, _listView, lblPreview, _txtPreview });

            _btnCopy = new Button { Text = "Copiar texto", Location = new Point(M, 12), Size = new Size(120, 34) };
            Theme.StylePrimary(_btnCopy);
            _btnCopy.Click += BtnCopy_Click;

            Button btnAddToDict = new Button { Text = "Criar regra no dicionário", Location = new Point(M + 128, 12), Size = new Size(180, 34) };
            Theme.StyleSecondary(btnAddToDict);
            btnAddToDict.Click += (s, e) =>
            {
                string selText = _txtPreview.SelectedText;
                if (string.IsNullOrWhiteSpace(selText))
                    selText = _txtPreview.Text;

                using (var form = new QuickDictionaryForm(selText))
                {
                    form.ShowDialog();
                }
            };

            _btnClear = new Button { Text = "Limpar histórico", Location = new Point(M + 316, 12), Size = new Size(130, 34) };
            Theme.StyleDanger(_btnClear);
            _btnClear.Click += BtnClear_Click;

            _btnClose = new Button { Text = "Fechar", Location = new Point(W - M - 100, 12), Size = new Size(100, 34), DialogResult = DialogResult.Cancel };
            Theme.StyleSecondary(_btnClose);
            _btnClose.Click += (s, e) => this.Close();

            footer.Controls.AddRange(new Control[] { _btnCopy, btnAddToDict, _btnClear, _btnClose });

            this.Controls.Add(body);
            this.Controls.Add(footer);
            this.Controls.Add(header);
            this.CancelButton = _btnClose;
        }

        private static Label AddStatCard(Panel parent, string caption, Rectangle bounds)
        {
            Panel card = Theme.CreateCard(bounds);
            Panel accent = new Panel { Location = new Point(0, 0), Size = new Size(4, bounds.Height), BackColor = Theme.Accent };
            Label lblValue = new Label { Text = "0", Font = Theme.StatValue, ForeColor = Theme.Navy, Location = new Point(16, 6), AutoSize = true };
            Label lblCaption = new Label { Text = caption, Font = Theme.Small, ForeColor = Theme.TextMuted, Location = new Point(18, 40), AutoSize = true };
            card.Controls.Add(accent);
            card.Controls.Add(lblValue);
            card.Controls.Add(lblCaption);
            parent.Controls.Add(card);
            return lblValue;
        }

        private void RefreshHistory()
        {
            _listView.BeginUpdate();
            _listView.Items.Clear();
            var history = HistoryRepository.GetAll();
            string query = _txtSearch.Text.Trim().ToLower();

            int totalWords = history.Sum(h => h.WordCount);
            int totalCount = history.Count;
            double estimatedMinutesSaved = Math.Round((totalWords / 40.0), 1);

            _lblStatCount.Text = totalCount.ToString("N0");
            _lblStatWords.Text = totalWords.ToString("N0");
            _lblStatMinutes.Text = "~" + estimatedMinutesSaved.ToString("N1");

            foreach (var item in history)
            {
                if (!string.IsNullOrEmpty(query))
                {
                    bool match = (item.FinalText != null && item.FinalText.ToLower().Contains(query)) ||
                                 (item.TargetProcess != null && item.TargetProcess.ToLower().Contains(query));
                    if (!match) continue;
                }

                ListViewItem lvi = new ListViewItem(item.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"));
                lvi.SubItems.Add(item.TargetProcess ?? "Desconhecido");
                lvi.SubItems.Add(string.Format("{0:F1}s", item.DurationSeconds));
                lvi.SubItems.Add(item.WordCount.ToString());
                lvi.SubItems.Add(item.FinalText ?? "");
                lvi.Tag = item;
                _listView.Items.Add(lvi);
            }
            Theme.ApplyZebra(_listView);
            _listView.EndUpdate();
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count > 0)
            {
                var item = _listView.SelectedItems[0].Tag as DictationHistoryItem;
                _txtPreview.Text = (item != null && item.FinalText != null) ? item.FinalText.Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            }
            else
            {
                _txtPreview.Text = "";
            }
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_txtPreview.Text))
            {
                ClipboardService.SetText(TextSanitizer.NormalizeForLegacyApps(_txtPreview.Text));
                MessageBox.Show("Texto copiado para a Área de Transferência!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Deseja realmente limpar todo o histórico de transcrições?", "Confirmar Exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                HistoryRepository.ClearAll();
                RefreshHistory();
                _txtPreview.Text = "";
            }
        }
    }
}
