using System;
using System.Drawing;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    public class PendingAudiosForm : Form
    {
        private ListView _listView;
        private Button _btnRetry;
        private Button _btnDelete;
        private Button _btnRetryAll;
        private Button _btnClose;

        public PendingAudiosForm()
        {
            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            this.Text = "Áudios Pendentes — Fila de Gravações";
            this.Size = new Size(680, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            _listView = new ListView
            {
                Location = new Point(12, 12),
                Size = new Size(640, 310),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };

            _listView.Columns.Add("Data / Hora", 140);
            _listView.Columns.Add("Duração", 80);
            _listView.Columns.Add("Status", 110);
            _listView.Columns.Add("Tentativas", 75);
            _listView.Columns.Add("Último Erro", 215);

            _btnRetry = new Button { Text = "Tentar Novamente", Location = new Point(12, 335), Size = new Size(130, 30), BackColor = Color.FromArgb(0, 122, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnRetry.FlatAppearance.BorderSize = 0;
            _btnRetry.Click += BtnRetry_Click;

            _btnDelete = new Button { Text = "Excluir", Location = new Point(150, 335), Size = new Size(90, 30), BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnDelete.FlatAppearance.BorderSize = 0;
            _btnDelete.Click += BtnDelete_Click;

            _btnRetryAll = new Button { Text = "Tentar Todos os Pendentes", Location = new Point(250, 335), Size = new Size(170, 30) };
            _btnRetryAll.Click += BtnRetryAll_Click;

            _btnClose = new Button { Text = "Fechar", Location = new Point(562, 335), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
            _btnClose.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[] { _listView, _btnRetry, _btnDelete, _btnRetryAll, _btnClose });
            this.CancelButton = _btnClose;
        }

        public void RefreshList()
        {
            _listView.Items.Clear();
            var items = PendingAudioRepository.GetAll();

            foreach (var item in items)
            {
                ListViewItem lvi = new ListViewItem(item.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss"));
                lvi.SubItems.Add(string.Format("{0:F1}s", item.DurationSeconds));
                lvi.SubItems.Add(item.Status.ToString());
                lvi.SubItems.Add(item.RetryCount.ToString());
                lvi.SubItems.Add(item.LastError ?? "-");
                lvi.Tag = item;

                if (item.Status == AudioQueueStatus.ERROR)
                    lvi.ForeColor = Color.DarkRed;
                else if (item.Status == AudioQueueStatus.UPLOADING || item.Status == AudioQueueStatus.TRANSCRIBING)
                    lvi.ForeColor = Color.DarkOrange;

                _listView.Items.Add(lvi);
            }
        }

        private async void BtnRetry_Click(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var item = _listView.SelectedItems[0].Tag as AudioQueueItem;
            if (item != null)
            {
                _btnRetry.Enabled = false;
                await RetryService.ProcessItemAsync(item);
                _btnRetry.Enabled = true;
                RefreshList();
            }
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var item = _listView.SelectedItems[0].Tag as AudioQueueItem;
            if (item != null)
            {
                if (MessageBox.Show("Tem certeza que deseja excluir esta gravação pendente?", "Confirmar Exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    PendingAudioRepository.DeleteItem(item.Id);
                    RefreshList();
                }
            }
        }

        private async void BtnRetryAll_Click(object sender, EventArgs e)
        {
            var pending = PendingAudioRepository.GetPendingItems();
            _btnRetryAll.Enabled = false;
            foreach (var item in pending)
            {
                await RetryService.ProcessItemAsync(item);
            }
            _btnRetryAll.Enabled = true;
            RefreshList();
        }
    }
}
