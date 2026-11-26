using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    /// <summary>Ícones de traço desenhados à mão (grade 24×24), no estilo do protótipo.</summary>
    public static class NavIcons
    {
        public const string Home = "home";
        public const string History = "history";
        public const string Book = "book";
        public const string Inbox = "inbox";
        public const string Sliders = "sliders";
        public const string Keyboard = "keyboard";
        public const string Key = "key";
        public const string Mic = "mic";
        public const string Refresh = "refresh";
        public const string Copy = "copy";
        public const string Plus = "plus";

        public static void Draw(Graphics g, string icon, RectangleF r, Color color, float stroke)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (Pen p = new Pen(color, stroke * 24f / r.Width))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                switch (icon)
                {
                    case Home:
                        g.DrawLines(p, new[] { new PointF(4, 11), new PointF(12, 4), new PointF(20, 11) });
                        g.DrawLines(p, new[] { new PointF(6, 10), new PointF(6, 20), new PointF(18, 20), new PointF(18, 10) });
                        break;
                    case History:
                        g.DrawEllipse(p, 3, 3, 18, 18);
                        g.DrawLines(p, new[] { new PointF(12, 7), new PointF(12, 12), new PointF(15, 14) });
                        break;
                    case Book:
                        g.DrawLines(p, new[] { new PointF(5, 17), new PointF(5, 4), new PointF(15, 4), new PointF(18, 7), new PointF(18, 20), new PointF(8, 20), new PointF(5, 17) });
                        g.DrawLines(p, new[] { new PointF(5, 17), new PointF(8, 14), new PointF(18, 14) });
                        break;
                    case Inbox:
                        g.DrawLines(p, new[] { new PointF(4, 13), new PointF(6, 5), new PointF(18, 5), new PointF(20, 13), new PointF(20, 19), new PointF(4, 19), new PointF(4, 13) });
                        g.DrawLines(p, new[] { new PointF(4, 13), new PointF(9, 13), new PointF(10, 15), new PointF(14, 15), new PointF(15, 13), new PointF(20, 13) });
                        break;
                    case Sliders:
                        g.DrawLine(p, 4, 7, 14, 7); g.DrawLine(p, 18, 7, 20, 7);
                        g.DrawLine(p, 4, 17, 6, 17); g.DrawLine(p, 10, 17, 20, 17);
                        g.DrawEllipse(p, 14, 5, 4, 4); g.DrawEllipse(p, 6, 15, 4, 4);
                        break;
                    case Keyboard:
                        using (GraphicsPath kb = Theme.RoundedRect(new RectangleF(3, 6, 18, 12), 2)) g.DrawPath(p, kb);
                        g.DrawLine(p, 7, 10, 7.1f, 10); g.DrawLine(p, 11, 10, 11.1f, 10); g.DrawLine(p, 15, 10, 15.1f, 10);
                        g.DrawLine(p, 7, 14, 17, 14);
                        break;
                    case Key:
                        g.DrawEllipse(p, 4, 11, 8, 8);
                        g.DrawLine(p, 11, 12, 20, 3); g.DrawLine(p, 16, 7, 19, 10);
                        break;
                    case Mic:
                        using (GraphicsPath m = Theme.RoundedRect(new RectangleF(9, 3, 6, 11), 3)) g.DrawPath(p, m);
                        g.DrawArc(p, 5, 4, 14, 14, 0, 180);
                        g.DrawLine(p, 12, 18, 12, 21);
                        break;
                    case Refresh:
                        g.DrawArc(p, 4, 4, 16, 16, 200, 250);
                        g.DrawLines(p, new[] { new PointF(4, 4), new PointF(4, 9), new PointF(9, 9) });
                        break;
                    case Copy:
                        using (GraphicsPath c1 = Theme.RoundedRect(new RectangleF(9, 9, 11, 11), 2)) g.DrawPath(p, c1);
                        g.DrawLines(p, new[] { new PointF(5, 15), new PointF(5, 6), new PointF(7, 4), new PointF(15, 4) });
                        break;
                    case Plus:
                        g.DrawLine(p, 12, 5, 12, 19); g.DrawLine(p, 5, 12, 19, 12);
                        break;
                }
            }
            g.Restore(state);
        }
    }

    /// <summary>Item da barra lateral escura: ícone + texto, fundo destacado quando ativo.</summary>
    public class NavButton : Control
    {
        private readonly string _icon;
        private bool _active;
        private bool _hover;

        public NavButton(string text, string icon)
        {
            Text = text;
            _icon = icon;
            Cursor = Cursors.Hand;
            BackColor = Theme.Sidebar;
            Font = new Font(Theme.FontFamily, 10F, FontStyle.Regular);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = text;
        }

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Sidebar);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_active || _hover)
            {
                Color bg = _active ? Theme.SidebarActive : Color.FromArgb(32, 35, 42);
                using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), 10))
                using (SolidBrush b = new SolidBrush(bg))
                    g.FillPath(b, path);
            }
            NavIcons.Draw(g, _icon, new RectangleF(12, (Height - 20) / 2f, 20, 20), _active ? Theme.SidebarIconActive : Theme.SidebarIcon, 1.75f);
            using (Font f = new Font(Theme.FontFamily, 10F, _active ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, f, new Rectangle(44, 0, Width - 48, Height), _active ? Color.White : Theme.SidebarText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(Theme.SidebarIconActive, 2))
                    g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }

    /// <summary>Página "Início" da janela principal (layout do protótipo; cresce com a janela maximizada).</summary>
    public class HomePage : Panel
    {
        public Action<int> NavigateTo;

        private readonly Theme.RoundPanel _howTo;
        private readonly Theme.RoundPanel _historyCard;
        private readonly Theme.RoundPanel _statusCard;
        private readonly Theme.RoundPanel _dictCard;
        private List<DictationHistoryItem> _history = new List<DictationHistoryItem>();
        private List<string> _words = new List<string>();
        private bool _hasKey;
        private string _hotkey = "F8";
        private readonly List<Rectangle> _copyRects = new List<Rectangle>();
        private Rectangle _updateRect;
        private string _copiedFeedback;

        private const int RowHeight = 54;

        public HomePage()
        {
            BackColor = Theme.Background;
            Padding = new Padding(32, 26, 28, 24);
            DoubleBuffered = true;

            // Coluna direita: estado e dicionário
            Panel right = new Panel { Dock = DockStyle.Right, Width = 318, BackColor = Theme.Background, Padding = new Padding(18, 0, 0, 0) };
            _statusCard = new Theme.RoundPanel { Dock = DockStyle.Top, Height = 214 };
            _statusCard.Paint += PaintStatus;
            _statusCard.MouseClick += (s, e) =>
            {
                if (_updateRect.Contains(e.Location) && SettingsForm.RequestUpdateCheck != null) SettingsForm.RequestUpdateCheck();
            };
            _statusCard.MouseMove += (s, e) => { _statusCard.Cursor = _updateRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default; };

            _dictCard = new Theme.RoundPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 18) };
            _dictCard.Paint += PaintDictionary;
            _dictCard.Resize += (s, e) => _dictCard.Invalidate();
            Button add = new Button { Text = "+  Adicionar palavra", Dock = DockStyle.Bottom, Height = 44 };
            add.FlatStyle = FlatStyle.Flat;
            add.BackColor = Color.FromArgb(250, 249, 255);
            add.ForeColor = Theme.AccentHover;
            add.Font = Theme.BodyBold;
            add.FlatAppearance.BorderColor = Color.FromArgb(169, 163, 230);
            add.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
            add.Cursor = Cursors.Hand;
            add.UseVisualStyleBackColor = false;
            add.Click += (s, e) =>
            {
                using (var form = new QuickDictionaryForm()) form.ShowDialog();
                RefreshData();
            };
            _dictCard.Controls.Add(add);

            right.Controls.Add(_dictCard);
            right.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = Theme.Background });
            right.Controls.Add(_statusCard);

            // Coluna principal
            Panel main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            Panel header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Background };
            header.Paint += (s, e) =>
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (Font f = new Font(Theme.FontFamily, 22F, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, "Início", f, new Point(-4, 0), Theme.Text);
                TextRenderer.DrawText(e.Graphics, "O VoxIA fica na bandeja e dita direto no programa que você estiver usando.",
                    Theme.Body, new Rectangle(0, 44, header.Width, 22), Theme.TextMuted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            header.Resize += (s, e) => header.Invalidate();

            _howTo = new Theme.RoundPanel { Dock = DockStyle.Top, Height = 248 };
            _howTo.Paint += PaintHowTo;

            _historyCard = new Theme.RoundPanel { Dock = DockStyle.Fill };
            _historyCard.Paint += PaintHistory;
            _historyCard.MouseClick += HistoryClick;
            _historyCard.MouseMove += (s, e) =>
            {
                bool over = _copyRects.Any(r => r.Contains(e.Location));
                _historyCard.Cursor = over ? Cursors.Hand : Cursors.Default;
            };

            main.Controls.Add(_historyCard);
            main.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 18, BackColor = Theme.Background });
            main.Controls.Add(_howTo);
            main.Controls.Add(header);

            Controls.Add(main);
            Controls.Add(right);

            RefreshData();
        }

        public void RefreshData()
        {
            try
            {
                _history = HistoryRepository.GetAll().OrderByDescending(h => h.Timestamp).Take(20).ToList();
            }
            catch { _history = new List<DictationHistoryItem>(); }
            try
            {
                _words = DictionaryService.GetEntries()
                    .Select(d => !string.IsNullOrWhiteSpace(d.Replacement) ? d.Replacement : d.Original)
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .Distinct().Take(30).ToList();
            }
            catch { _words = new List<string>(); }
            try { _hasKey = !string.IsNullOrWhiteSpace(SecureCredentialService.GetApiKey()); } catch { _hasKey = false; }
            _hotkey = !string.IsNullOrEmpty(SettingsService.Current.HotkeyDisplay) ? SettingsService.Current.HotkeyDisplay : "F8";
            _howTo.Invalidate();
            _historyCard.Invalidate();
            _statusCard.Invalidate();
            _dictCard.Invalidate();
        }

        // ---------- Desenho ----------

        private static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        private void PaintHowTo(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Prep(g);
            int w = _howTo.Width;
            int illoW = w > 760 ? 252 : 0;
            int textW = w - 48 - (illoW > 0 ? illoW + 28 : 0);

            TextRenderer.DrawText(g, "COMO DITAR", Theme.Eyebrow, new Point(24, 22), Theme.AccentHover);
            using (Font h = new Font(Theme.FontFamily, 17F, FontStyle.Bold))
                TextRenderer.DrawText(g, "Aperte " + _hotkey + " em qualquer programa e fale.", h, new Rectangle(22, 42, textW, 34), Theme.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, "Word, WhatsApp Web, e-mail, sistemas da empresa. Aperte " + _hotkey + " de novo e o texto aparece escrito no lugar, com pontuação e ortografia corrigidas.",
                Theme.Body, new Rectangle(24, 82, textW, 40), Theme.TextMuted, TextFormatFlags.WordBreak);

            string[][] rows = {
                new[] { _hotkey, "Começar e terminar o ditado" },
                new[] { "Esc", "Cancelar a gravação" },
                new[] { "Ctrl + Shift + W", "Ensinar a palavra selecionada" }
            };
            int y = 132;
            foreach (string[] r in rows)
            {
                int kw = Math.Max(40, TextRenderer.MeasureText(r[0], Theme.BodyBold).Width + 18);
                Rectangle kr = new Rectangle(24, y, kw, 26);
                using (GraphicsPath p = Theme.RoundedRect(kr, 6))
                {
                    using (SolidBrush b = new SolidBrush(Theme.Background)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.BorderStrong)) g.DrawPath(pen, p);
                }
                using (Pen under = new Pen(Theme.BorderStrong, 2)) g.DrawLine(under, kr.X + 4, kr.Bottom, kr.Right - 4, kr.Bottom);
                TextRenderer.DrawText(g, r[0], Theme.BodyBold, kr, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, r[1], Theme.Body, new Point(kr.Right + 12, y + 5), Theme.TextSecondary);
                y += 34;
            }

            if (illoW > 0) PaintIllustration(g, new Rectangle(w - 24 - illoW, 24, illoW, 200));
        }

        private static void PaintIllustration(Graphics g, Rectangle r)
        {
            using (GraphicsPath p = Theme.RoundedRect(r, 14))
            {
                using (SolidBrush b = new SolidBrush(Theme.Background)) g.FillPath(b, p);
                GraphicsState st = g.Save();
                g.SetClip(p);
                using (SolidBrush bar = new SolidBrush(Color.FromArgb(233, 231, 224))) g.FillRectangle(bar, r.X, r.Y, r.Width, 26);
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(201, 197, 184)))
                    for (int i = 0; i < 3; i++) g.FillEllipse(dot, r.X + 10 + i * 12, r.Y + 10, 7, 7);
                TextRenderer.DrawText(g, "Documento.docx", Theme.Small, new Point(r.X + 50, r.Y + 5), Theme.TextMuted);
                float[] widths = { 0.70f, 0.92f, 0.55f };
                using (SolidBrush line = new SolidBrush(Theme.BorderStrong))
                    for (int i = 0; i < widths.Length; i++)
                        using (GraphicsPath lp = Theme.RoundedRect(new RectangleF(r.X + 16, r.Y + 42 + i * 17, (r.Width - 32) * widths[i], 8), 4)) g.FillPath(line, lp);
                using (SolidBrush acc = new SolidBrush(Theme.Accent))
                {
                    using (GraphicsPath lp = Theme.RoundedRect(new RectangleF(r.X + 16, r.Y + 93, (r.Width - 32) * 0.38f, 8), 4)) g.FillPath(acc, lp);
                    g.FillRectangle(acc, r.X + 18 + (r.Width - 32) * 0.38f, r.Y + 89, 2, 16);
                }
                // Pílula de gravação
                Rectangle pill = new Rectangle(r.X + (r.Width - 130) / 2, r.Bottom - 50, 130, 34);
                using (GraphicsPath pp = Theme.RoundedRect(pill, 17))
                using (SolidBrush pb = new SolidBrush(Theme.Sidebar)) g.FillPath(pb, pp);
                using (SolidBrush red = new SolidBrush(Color.FromArgb(240, 82, 94))) g.FillEllipse(red, pill.X + 12, pill.Y + 13, 8, 8);
                int[] hs = { 6, 12, 8, 15, 7, 12, 5 };
                using (SolidBrush wave = new SolidBrush(Theme.SidebarIconActive))
                    for (int i = 0; i < hs.Length; i++) g.FillRectangle(wave, pill.X + 28 + i * 5, pill.Y + 17 - hs[i] / 2, 3, hs[i]);
                TextRenderer.DrawText(g, "00:18", Theme.BodyBold, new Point(pill.X + 70, pill.Y + 9), Color.White);
                g.Restore(st);
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
        }

        private void PaintHistory(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Prep(g);
            int w = _historyCard.Width;
            _copyRects.Clear();

            using (Font t = new Font(Theme.FontFamily, 12F, FontStyle.Bold))
                TextRenderer.DrawText(g, "Últimos ditados", t, new Point(22, 16), Theme.Text);
            string right = _copiedFeedback ?? HistoryRetentionText();
            Size rs = TextRenderer.MeasureText(right, Theme.Small);
            TextRenderer.DrawText(g, right, Theme.Small, new Point(w - 24 - rs.Width, 20), _copiedFeedback != null ? Theme.Success : Theme.TextMuted);

            int y = 52;
            if (_history.Count == 0)
            {
                using (Pen d = new Pen(Theme.Divider)) g.DrawLine(d, 24, y, w - 24, y);
                TextRenderer.DrawText(g, "Nenhum ditado ainda. Aperte " + _hotkey + " em qualquer programa para começar.", Theme.Body,
                    new Point(24, y + 18), Theme.TextMuted);
                return;
            }

            foreach (DictationHistoryItem item in _history)
            {
                if (y + RowHeight > _historyCard.Height - 4) break;
                using (Pen d = new Pen(Theme.Divider)) g.DrawLine(d, 24, y, w - 24, y);
                int cy = y + RowHeight / 2;

                TextRenderer.DrawText(g, item.Timestamp.ToString("HH:mm"), Theme.Body, new Rectangle(24, y, 48, RowHeight), Theme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                Rectangle copy = new Rectangle(w - 24 - 36, cy - 18, 36, 36);
                NavIcons.Draw(g, NavIcons.Copy, new RectangleF(copy.X + 9, copy.Y + 9, 18, 18), Theme.TextMuted, 1.75f);
                _copyRects.Add(copy);

                string app = FriendlyApp(item.TargetProcess);
                int chipRight = copy.X - 12;
                int textRight = chipRight;
                if (!string.IsNullOrEmpty(app))
                {
                    Size cs = TextRenderer.MeasureText(app, Theme.Small);
                    Rectangle chip = new Rectangle(chipRight - cs.Width - 16, cy - 12, cs.Width + 16, 24);
                    using (GraphicsPath cp = Theme.RoundedRect(chip, 12))
                    using (SolidBrush cb = new SolidBrush(Theme.SurfaceAlt)) g.FillPath(cb, cp);
                    TextRenderer.DrawText(g, app, Theme.Small, chip, Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    textRight = chip.X - 14;
                }

                string text = (item.FinalText ?? "").Replace("\r", " ").Replace("\n", " ");
                TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(86, y, Math.Max(10, textRight - 86), RowHeight), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                y += RowHeight;
            }
        }

        private static string HistoryRetentionText()
        {
            int days = SettingsService.Current.HistoryRetentionDays;
            if (days < 0) return "Guardados sem limite de tempo";
            if (days == 0) return "Histórico desligado nas Configurações";
            return "Apagados do aparelho após " + days + (days == 1 ? " dia" : " dias");
        }

        private void HistoryClick(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < _copyRects.Count && i < _history.Count; i++)
            {
                if (!_copyRects[i].Contains(e.Location)) continue;
                try
                {
                    Clipboard.SetText(_history[i].FinalText ?? "");
                    _copiedFeedback = "Texto copiado";
                }
                catch { _copiedFeedback = "Não deu para copiar"; }
                _historyCard.Invalidate();
                Timer t = new Timer { Interval = 1800 };
                t.Tick += (s, a) => { t.Stop(); t.Dispose(); _copiedFeedback = null; _historyCard.Invalidate(); };
                t.Start();
                return;
            }
            if (e.Y < 50 && NavigateTo != null) NavigateTo(SettingsForm.PageHistory);
        }

        private static string FriendlyApp(string process)
        {
            if (string.IsNullOrWhiteSpace(process)) return "";
            string p = process.Trim().ToLowerInvariant();
            if (p.EndsWith(".exe")) p = p.Substring(0, p.Length - 4);
            switch (p)
            {
                case "winword": return "Word";
                case "excel": return "Excel";
                case "powerpnt": return "PowerPoint";
                case "outlook": case "olk": return "Outlook";
                case "chrome": return "Chrome";
                case "msedge": return "Edge";
                case "firefox": return "Firefox";
                case "whatsapp": case "whatsapp.root": return "WhatsApp";
                case "notepad": return "Bloco de notas";
                case "teams": case "ms-teams": return "Teams";
            }
            string name = process.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name.Length > 18 ? name.Substring(0, 18) : name;
        }

        private void PaintStatus(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Prep(g);
            int w = _statusCard.Width;
            bool allOk = _hasKey;
            using (Font t = new Font(Theme.FontFamily, 12F, FontStyle.Bold))
                TextRenderer.DrawText(g, allOk ? "Tudo certo" : "Falta a chave", t, new Point(18, 16), Theme.Text);

            bool boost = SettingsService.Current.BoostMicrophoneDuringDictation;
            object[][] rows = {
                new object[] { NavIcons.Key, "Chave de API", _hasKey ? "Conectada" : "Cole a chave nas Configurações", _hasKey },
                new object[] { NavIcons.Mic, "Microfone", boost ? "Sobe a 100% só durante o ditado" : "Usa o volume do sistema", true },
                new object[] { NavIcons.Refresh, "Atualizações", "Clique para procurar uma versão nova", true }
            };
            int y = 50;
            for (int i = 0; i < rows.Length; i++)
            {
                bool ok = (bool)rows[i][3];
                using (Pen d = new Pen(Theme.Divider)) g.DrawLine(d, 18, y, w - 18, y);
                Rectangle ic = new Rectangle(18, y + 10, 32, 32);
                using (GraphicsPath p = Theme.RoundedRect(ic, 10))
                using (SolidBrush b = new SolidBrush(ok ? Theme.SuccessSoft : Theme.DangerSoft)) g.FillPath(b, p);
                NavIcons.Draw(g, (string)rows[i][0], new RectangleF(ic.X + 7.5f, ic.Y + 7.5f, 17, 17), ok ? Theme.Success : Theme.Danger, 1.9f);
                TextRenderer.DrawText(g, (string)rows[i][1], Theme.BodyBold, new Point(62, y + 9), Theme.Text);
                TextRenderer.DrawText(g, (string)rows[i][2], Theme.Small, new Rectangle(62, y + 27, w - 80, 18), Theme.TextMuted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                if (i == 2) _updateRect = new Rectangle(0, y, w, 52);
                y += 52;
            }
        }

        private void PaintDictionary(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Prep(g);
            int w = _dictCard.Width;
            using (Font t = new Font(Theme.FontFamily, 12F, FontStyle.Bold))
                TextRenderer.DrawText(g, "Dicionário", t, new Point(18, 16), Theme.Text);
            TextRenderer.DrawText(g, "Palavras que você ensinou. O VoxIA passa a escrevê-las certo.", Theme.Small,
                new Rectangle(18, 44, w - 36, 34), Theme.TextMuted, TextFormatFlags.WordBreak);

            int x = 18, y = 84;
            int maxY = _dictCard.Height - 18 - 44 - 12;
            if (_words.Count == 0)
            {
                TextRenderer.DrawText(g, "Nenhuma palavra ainda.", Theme.Body, new Point(18, y), Theme.TextMuted);
                return;
            }
            foreach (string word in _words)
            {
                Size s = TextRenderer.MeasureText(word, Theme.BodyBold);
                int cw = Math.Min(w - 36, s.Width + 22);
                if (x + cw > w - 18) { x = 18; y += 36; }
                if (y + 28 > maxY) break;
                Rectangle chip = new Rectangle(x, y, cw, 28);
                using (GraphicsPath p = Theme.RoundedRect(chip, 14))
                using (SolidBrush b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, p);
                TextRenderer.DrawText(g, word, Theme.BodyBold, chip, Theme.AccentHover,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                x += cw + 8;
            }
        }
    }
}
