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
            const int W = 720;
            const int M = 22;
            Theme.ApplyForm(this, "Áudios Pendentes — VoxIA", new Size(W, 460));

            Panel header = Theme.CreateHeader("Áudios pendentes", "Gravações salvas localmente aguardando envio. Nenhum ditado é perdido sem conexão.");
            Panel footer = Theme.CreateFooter();
            footer.Width = W;
            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            _listView = new ListView
            {
                Location = new Point(M, 18),
                Size = new Size(W - M * 2, 300),
                MultiSelect = false
            };
            Theme.StyleListView(_listView);

            _listView.Columns.Add("Data / hora", 140);
            _listView.Columns.Add("Duração", 80);
            _listView.Columns.Add("Status", 120);
            _listView.Columns.Add("Tentativas", 80);
            _listView.Columns.Add("Último erro", 250);
            body.Controls.Add(_listView);

            _btnRetry = new Button { Text = "Tentar novamente", Location = new Point(M, 12), Size = new Size(140, 34) };
            Theme.StylePrimary(_btnRetry);
            _btnRetry.Click += BtnRetry_Click;

            _btnRetryAll = new Button { Text = "Tentar todos os pendentes", Location = new Point(M + 148, 12), Size = new Size(180, 34) };
            Theme.StyleSecondary(_btnRetryAll);
            _btnRetryAll.Click += BtnRetryAll_Click;

            _btnDelete = new Button { Text = "Excluir", Location = new Point(M + 336, 12), Size = new Size(90, 34) };
            Theme.StyleDanger(_btnDelete);
            _btnDelete.Click += BtnDelete_Click;

            _btnClose = new Button { Text = "Fechar", Location = new Point(W - M - 100, 12), Size = new Size(100, 34), DialogResult = DialogResult.Cancel };
            Theme.StyleSecondary(_btnClose);
            _btnClose.Click += (s, e) => this.Close();

            footer.Controls.AddRange(new Control[] { _btnRetry, _btnRetryAll, _btnDelete, _btnClose });

            this.Controls.Add(body);
            this.Controls.Add(footer);
            this.Controls.Add(header);
            this.CancelButton = _btnClose;
        }

        private static string StatusLabel(AudioQueueStatus status)
        {
            switch (status)
            {
                case AudioQueueStatus.PENDING: return "Aguardando envio";
                case AudioQueueStatus.UPLOADING: return "Enviando";
                case AudioQueueStatus.TRANSCRIBING: return "Transcrevendo";
                case AudioQueueStatus.CORRECTING: return "Corrigindo texto";
                case AudioQueueStatus.COMPLETED: return "Concluído";
                case AudioQueueStatus.ERROR: return "Erro";
                default: return status.ToString();
            }
        }

        public void RefreshList()
        {
            _listView.BeginUpdate();
            _listView.Items.Clear();
            var items = PendingAudioRepository.GetAll();

            foreach (var item in items)
            {
                ListViewItem lvi = new ListViewItem(item.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss"));
                lvi.SubItems.Add(string.Format("{0:F1}s", item.DurationSeconds));
                lvi.SubItems.Add(StatusLabel(item.Status));
                lvi.SubItems.Add(item.RetryCount.ToString());
                lvi.SubItems.Add(item.LastError ?? "-");
                lvi.Tag = item;
                _listView.Items.Add(lvi);
            }

            Theme.ApplyZebra(_listView);
            foreach (ListViewItem lvi in _listView.Items)
            {
                var item = (AudioQueueItem)lvi.Tag;
                if (item.Status == AudioQueueStatus.ERROR)
                    lvi.ForeColor = Theme.Danger;
                else if (item.Status == AudioQueueStatus.UPLOADING || item.Status == AudioQueueStatus.TRANSCRIBING || item.Status == AudioQueueStatus.CORRECTING)
                    lvi.ForeColor = Theme.Warning;
            }
            _listView.EndUpdate();
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
