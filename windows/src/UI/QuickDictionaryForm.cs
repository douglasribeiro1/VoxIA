using System;
using System.Drawing;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    public class QuickDictionaryForm : Form
    {
        private TextBox _txtOriginal;
        private TextBox _txtReplacement;
        private CheckBox _chkVocabularyOnly;
        private Button _btnSave;
        private Button _btnCancel;

        public QuickDictionaryForm(string prefilledText = null)
        {
            InitializeComponent();
            SetupInitialValues(prefilledText);
        }

        private void InitializeComponent()
        {
            const int W = 460;
            const int M = 22;
            Theme.ApplyForm(this, "Adicionar ao Dicionário — VoxIA", new Size(W, 296));
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.TopMost = true;

            string hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";
            Panel header = Theme.CreateHeader("Dicionário de palavras", string.Format("Atalho rápido: Ctrl + Shift + W  ·  Ditado: {0}", hotkey));
            Panel footer = Theme.CreateFooter();
            footer.Width = W;
            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            Label lblOrig = new Label
            {
                Text = "Palavra ou erro de transcrição (de)",
                Location = new Point(M, 16),
                AutoSize = true,
                Font = Theme.BodyBold
            };

            _txtOriginal = new TextBox
            {
                Location = new Point(M, 38),
                Width = W - M * 2
            };
            Theme.StyleTextBox(_txtOriginal);

            Label lblRepl = new Label
            {
                Text = "Substituição correta (para)",
                Location = new Point(M, 74),
                AutoSize = true,
                Font = Theme.BodyBold
            };

            _txtReplacement = new TextBox
            {
                Location = new Point(M, 96),
                Width = W - M * 2
            };
            Theme.StyleTextBox(_txtReplacement);

            _chkVocabularyOnly = new CheckBox
            {
                Text = "Apenas termo de vocabulário (sem substituição direta)",
                Location = new Point(M, 134),
                AutoSize = true,
                ForeColor = Theme.TextMuted,
                Cursor = Cursors.Hand
            };
            _chkVocabularyOnly.CheckedChanged += (s, e) =>
            {
                if (_chkVocabularyOnly.Checked)
                {
                    _txtReplacement.Text = _txtOriginal.Text;
                    _txtReplacement.Enabled = false;
                }
                else
                {
                    _txtReplacement.Enabled = true;
                }
            };

            body.Controls.AddRange(new Control[] { lblOrig, _txtOriginal, lblRepl, _txtReplacement, _chkVocabularyOnly });

            // Buttons
            _btnCancel = new Button { Text = "Cancelar (Esc)", Size = new Size(110, 34) };
            Theme.StyleSecondary(_btnCancel);
            _btnCancel.Location = new Point(W - M - _btnCancel.Width, 12);
            _btnCancel.Click += (s, e) => this.Close();

            _btnSave = new Button { Text = "Salvar (Enter)", Size = new Size(120, 34) };
            Theme.StylePrimary(_btnSave);
            _btnSave.Location = new Point(_btnCancel.Left - _btnSave.Width - 10, 12);
            _btnSave.Click += BtnSave_Click;

            footer.Controls.Add(_btnSave);
            footer.Controls.Add(_btnCancel);

            this.Controls.Add(body);
            this.Controls.Add(footer);
            this.Controls.Add(header);

            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private void SetupInitialValues(string prefilledText)
        {
            if (!string.IsNullOrWhiteSpace(prefilledText))
            {
                string clean = prefilledText.Trim();
                _txtOriginal.Text = clean;
                _txtReplacement.Text = clean;

                this.ActiveControl = _txtReplacement;
                _txtReplacement.SelectAll();
            }
            else
            {
                this.ActiveControl = _txtOriginal;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string orig = _txtOriginal.Text.Trim();
            string repl = _txtReplacement.Text.Trim();

            if (string.IsNullOrEmpty(orig) && string.IsNullOrEmpty(repl))
            {
                MessageBox.Show("Por favor, preencha a palavra antes de salvar.", "Dicionário VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(orig)) orig = repl;
            if (string.IsNullOrEmpty(repl)) repl = orig;

            DictionaryService.AddOrUpdate(orig, repl, _chkVocabularyOnly.Checked);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
