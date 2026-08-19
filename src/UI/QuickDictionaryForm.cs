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
            this.Text = "Adicionar ao Dicionário — VoxIA";
            this.Size = new Size(460, 290);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 249, 252);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.TopMost = true;

            Panel header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(460, 55),
                BackColor = Color.White
            };

            Label lblTitle = new Label
            {
                Text = "📖 Dicionário de Palavras & Substituições",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Location = new Point(16, 10),
                AutoSize = true
            };

            Label lblSubtitle = new Label
            {
                Text = "Atalho rápido: Ctrl + Shift + W | Ditado: Ctrl + Win",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
                ForeColor = Color.FromArgb(108, 117, 125),
                Location = new Point(17, 31),
                AutoSize = true
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSubtitle);

            // Labels and TextBoxes
            Label lblOrig = new Label
            {
                Text = "Palavra ou Erro de Transcrição (De):",
                Location = new Point(20, 68),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _txtOriginal = new TextBox
            {
                Location = new Point(20, 88),
                Size = new Size(405, 25),
                Font = new Font("Segoe UI", 9.5F)
            };

            Label lblRepl = new Label
            {
                Text = "Substituição Correta (Para):",
                Location = new Point(20, 122),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _txtReplacement = new TextBox
            {
                Location = new Point(20, 142),
                Size = new Size(405, 25),
                Font = new Font("Segoe UI", 9.5F)
            };

            _chkVocabularyOnly = new CheckBox
            {
                Text = "Apenas termo de vocabulário (sem substituição direta)",
                Location = new Point(20, 175),
                AutoSize = true,
                ForeColor = Color.FromArgb(73, 80, 87)
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

            // Buttons
            _btnSave = new Button
            {
                Text = "Salvar (Enter)",
                Location = new Point(220, 210),
                Size = new Size(110, 32),
                BackColor = Color.FromArgb(0, 122, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += BtnSave_Click;

            _btnCancel = new Button
            {
                Text = "Cancelar (Esc)",
                Location = new Point(340, 210),
                Size = new Size(85, 32),
                BackColor = Color.FromArgb(230, 233, 240),
                ForeColor = Color.FromArgb(33, 37, 41),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderSize = 0;
            _btnCancel.Click += (s, e) => this.Close();

            this.Controls.Add(header);
            this.Controls.Add(lblOrig);
            this.Controls.Add(_txtOriginal);
            this.Controls.Add(lblRepl);
            this.Controls.Add(_txtReplacement);
            this.Controls.Add(_chkVocabularyOnly);
            this.Controls.Add(_btnSave);
            this.Controls.Add(_btnCancel);

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
