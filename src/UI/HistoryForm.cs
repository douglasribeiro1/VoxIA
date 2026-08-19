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
        private Label _lblStats;
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
            this.Text = "Histórico de Transcrições & Estatísticas — VoxIA";
            this.Size = new Size(740, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            _lblStats = new Label
            {
                Text = "Carregando estatísticas...",
                Location = new Point(12, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 122, 255)
            };

            Label lblSearch = new Label { Text = "Buscar:", Location = new Point(460, 14), AutoSize = true };
            _txtSearch = new TextBox { Location = new Point(515, 11), Width = 195 };
            _txtSearch.TextChanged += (s, e) => RefreshHistory();

            _listView = new ListView
            {
                Location = new Point(12, 42),
                Size = new Size(700, 220),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };

            _listView.Columns.Add("Data / Hora", 135);
            _listView.Columns.Add("Aplicativo Destino", 140);
            _listView.Columns.Add("Duração", 70);
            _listView.Columns.Add("Palavras", 75);
            _listView.Columns.Add("Texto Transcrito", 260);

            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            Label lblPreview = new Label { Text = "Texto Final Corrigido:", Location = new Point(12, 272), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _txtPreview = new TextBox
            {
                Location = new Point(12, 292),
                Size = new Size(700, 120),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White
            };

            _btnCopy = new Button { Text = "Copiar Texto", Location = new Point(12, 425), Size = new Size(130, 32), BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnCopy.FlatAppearance.BorderSize = 0;
            _btnCopy.Click += BtnCopy_Click;

            Button btnAddToDict = new Button { Text = "📖 Criar Regra no Dicionário", Location = new Point(150, 425), Size = new Size(180, 32) };
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

            _btnClear = new Button { Text = "Limpar Histórico", Location = new Point(340, 425), Size = new Size(130, 32) };
            _btnClear.Click += BtnClear_Click;

            _btnClose = new Button { Text = "Fechar", Location = new Point(622, 425), Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            _btnClose.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[] { _lblStats, lblSearch, _txtSearch, _listView, lblPreview, _txtPreview, _btnCopy, btnAddToDict, _btnClear, _btnClose });
            this.CancelButton = _btnClose;
        }

        private void RefreshHistory()
        {
            _listView.Items.Clear();
            var history = HistoryRepository.GetAll();
            string query = _txtSearch.Text.Trim().ToLower();

            int totalWords = history.Sum(h => h.WordCount);
            int totalCount = history.Count;
            double estimatedMinutesSaved = Math.Round((totalWords / 40.0), 1);

            _lblStats.Text = string.Format("Total: {0} ditados | {1} palavras | ~{2} minutos economizados digitando", totalCount, totalWords, estimatedMinutesSaved);

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
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count > 0)
            {
                var item = _listView.SelectedItems[0].Tag as DictationHistoryItem;
                _txtPreview.Text = item != null ? item.FinalText : "";
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
                ClipboardService.SetText(_txtPreview.Text);
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
