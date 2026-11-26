using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxIA.UI
{
    /// <summary>
    /// Identidade visual única do VoxIA: tema escuro com acento laranja-avermelhado, títulos em serifa itálica,
    /// cartões arredondados e componentes reutilizáveis (cabeçalho, rodapé, cartões, botões, listas e menu da bandeja).
    /// </summary>
    public static class Theme
    {
        // Paleta
        public static readonly Color Background = Color.FromArgb(14, 15, 18);
        public static readonly Color Surface = Color.FromArgb(22, 24, 29);
        public static readonly Color SurfaceAlt = Color.FromArgb(30, 33, 39);
        public static readonly Color Border = Color.FromArgb(38, 42, 51);
        public static readonly Color Text = Color.FromArgb(242, 239, 233);
        public static readonly Color TextMuted = Color.FromArgb(138, 143, 152);
        public static readonly Color Accent = Color.FromArgb(255, 90, 54);
        public static readonly Color AccentHover = Color.FromArgb(232, 74, 39);
        public static readonly Color AccentSoft = Color.FromArgb(42, 23, 19);
        public static readonly Color Cream = Color.FromArgb(242, 237, 228);
        public static readonly Color OnAccent = Color.FromArgb(20, 16, 14);
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);
        public static readonly Color DangerHover = Color.FromArgb(239, 68, 68);
        public static readonly Color Success = Color.FromArgb(52, 211, 153);
        public static readonly Color Warning = Color.FromArgb(251, 191, 36);

        // Títulos em destaque (antes azul-marinho; agora o texto claro do tema)
        public static readonly Color Navy = Text;
        public static readonly Color NavyLight = TextMuted;

        // Tipografia
        public const string FontFamily = "Segoe UI";
        public const string SerifFamily = "Georgia";
        public static readonly Font Body = new Font(FontFamily, 9F, FontStyle.Regular);
        public static readonly Font BodyBold = new Font(FontFamily, 9F, FontStyle.Bold);
        public static readonly Font Small = new Font(FontFamily, 8.25F, FontStyle.Regular);
        public static readonly Font Eyebrow = new Font(FontFamily, 7.5F, FontStyle.Regular);
        public static readonly Font Heading = new Font(FontFamily, 13F, FontStyle.Bold);
        public static readonly Font SectionTitle = new Font(SerifFamily, 20F, FontStyle.Italic);
        public static readonly Font CardTitle = new Font(FontFamily, 9.75F, FontStyle.Bold);
        public static readonly Font HeaderTitle = new Font(SerifFamily, 15F, FontStyle.Italic);
        public static readonly Font HeaderSubtitle = new Font(FontFamily, 8.5F, FontStyle.Regular);
        public static readonly Font StatValue = new Font(SerifFamily, 18F, FontStyle.Italic);

        public const int HeaderHeight = 64;
        public const int FooterHeight = 58;

        public static void ApplyForm(Form form, string title, Size clientSize)
        {
            form.Text = title;
            form.ClientSize = clientSize;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = Body;
            TrySetAppIcon(form);
        }

        public static void TrySetAppIcon(Form form)
        {
            try
            {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                form.ShowIcon = true;
            }
            catch { }
        }

        /// <summary>Faixa superior com título em serifa itálica e subtítulo, separada por uma linha sutil.</summary>
        public static Panel CreateHeader(string title, string subtitle)
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Background };
            header.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Border))
                    e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            Label lblTitle = new Label
            {
                Text = title,
                Font = HeaderTitle,
                ForeColor = Text,
                AutoSize = true,
                Location = new Point(22, 10),
                BackColor = Color.Transparent
            };
            Label lblSubtitle = new Label
            {
                Text = subtitle,
                Font = HeaderSubtitle,
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(24, 40),
                BackColor = Color.Transparent
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSubtitle);
            return header;
        }

        /// <summary>Rodapé com linha divisória superior, para os botões de ação.</summary>
        public static Panel CreateFooter()
        {
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = FooterHeight, BackColor = Background };
            footer.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Border))
                    e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            return footer;
        }

        /// <summary>Cartão escuro arredondado com borda sutil.</summary>
        public static Panel CreateCard(Rectangle bounds)
        {
            Panel card = new Panel { Bounds = bounds, BackColor = Surface };
            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new RectangleF(0.5f, 0.5f, card.Width - 1, card.Height - 1), 14))
                using (Pen p = new Pen(Border))
                    e.Graphics.DrawPath(p, path);
            };
            return card;
        }

        public static Label CreateCardTitle(string text, Point location)
        {
            return new Label { Text = text, Font = CardTitle, ForeColor = Text, AutoSize = true, Location = location, BackColor = Color.Transparent };
        }

        /// <summary>Texto pequeno em maiúsculas acima de um título de página ("PREFERÊNCIAS").</summary>
        public static Label CreateEyebrow(string text, Point location)
        {
            return new Label { Text = text.ToUpperInvariant(), Font = Eyebrow, ForeColor = TextMuted, AutoSize = true, Location = location, BackColor = Color.Transparent };
        }

        /// <summary>Título grande de página em serifa itálica.</summary>
        public static Label CreatePageTitle(string text, Point location)
        {
            return new Label { Text = text, Font = SectionTitle, ForeColor = Text, AutoSize = true, Location = location, BackColor = Color.Transparent };
        }

        private static ToolTip _helpTips;

        /// <summary>
        /// Ícone "?" que mostra a explicação ao passar o mouse (ou ao clicar). Deixa a tela limpa:
        /// a explicação só aparece para quem quiser.
        /// </summary>
        public static Label CreateHelpIcon(string text, Point location)
        {
            if (_helpTips == null)
            {
                _helpTips = new ToolTip { AutoPopDelay = 30000, InitialDelay = 150, ReshowDelay = 100, ShowAlways = true };
            }
            Label icon = new Label
            {
                Text = "?",
                Font = new Font(FontFamily, 8.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = SurfaceAlt,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(20, 20),
                Location = location,
                Cursor = Cursors.Help
            };
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(0, 0, 20, 20);
                icon.Region = new Region(path);
            }
            _helpTips.SetToolTip(icon, text);
            icon.Click += (s, e) => _helpTips.Show(text, icon, 24, 0, 15000);
            return icon;
        }

        public static Label CreateHint(string text, Point location, int width)
        {
            Label lbl = new Label { Text = text, Font = Small, ForeColor = TextMuted, Location = location, BackColor = Color.Transparent };
            if (width > 0)
            {
                // Altura calculada com quebra de linha, para o texto nunca ficar cortado
                Size measured = TextRenderer.MeasureText(text, Small, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
                lbl.AutoSize = false;
                lbl.Size = new Size(width, measured.Height + 4);
            }
            else
            {
                lbl.AutoSize = true;
            }
            return lbl;
        }

        public static Panel CreateDivider(Point location, int width)
        {
            return new Panel { Location = location, Size = new Size(width, 1), BackColor = Border };
        }

        /// <summary>Tecla desenhada como um pequeno bloco arredondado (ex.: "Ctrl", "Esc").</summary>
        public static Panel CreateKeyCap(string key, Point location)
        {
            int width = TextRenderer.MeasureText(key, BodyBold).Width + 18;
            Panel cap = new Panel { Location = location, Size = new Size(width, 24), BackColor = Color.Transparent };
            cap.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new RectangleF(0.5f, 0.5f, cap.Width - 1, cap.Height - 1), 7))
                {
                    using (SolidBrush b = new SolidBrush(SurfaceAlt))
                        e.Graphics.FillPath(b, path);
                    using (Pen p = new Pen(Border))
                        e.Graphics.DrawPath(p, path);
                }
                TextRenderer.DrawText(e.Graphics, key, BodyBold, cap.ClientRectangle, Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            };
            return cap;
        }

        /// <summary>Retângulo com cantos arredondados, para desenho manual.</summary>
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------- Botões ----------

        public static void StylePrimary(Button b)
        {
            StyleFlat(b, Accent, AccentHover, OnAccent, Accent);
            b.Font = BodyBold;
        }

        public static void StyleSecondary(Button b)
        {
            StyleFlat(b, SurfaceAlt, Color.FromArgb(40, 44, 52), Text, Border);
        }

        public static void StyleDanger(Button b)
        {
            StyleFlat(b, SurfaceAlt, AccentSoft, Danger, Color.FromArgb(90, 52, 52));
        }

        public static void StyleDangerSolid(Button b)
        {
            StyleFlat(b, Danger, DangerHover, OnAccent, Danger);
            b.Font = BodyBold;
        }

        private static void StyleFlat(Button b, Color back, Color hover, Color fore, Color border)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = fore;
            b.FlatAppearance.BorderColor = border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            if (b.Height < 30) b.Height = 32;
        }

        // ---------- Listas ----------

        public static void StyleListView(ListView lv)
        {
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.HideSelection = false;
            lv.BorderStyle = BorderStyle.FixedSingle;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.BackColor = Surface;
            lv.ForeColor = Text;
            lv.Font = Body;

            // Cabeçalho desenhado à mão, para acompanhar o tema escuro
            lv.OwnerDraw = true;
            lv.DrawItem += (s, e) => e.DrawDefault = true;
            lv.DrawSubItem += (s, e) => e.DrawDefault = true;
            lv.DrawColumnHeader += (s, e) =>
            {
                using (SolidBrush b = new SolidBrush(Surface))
                    e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Small,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
        }

        /// <summary>Linhas zebradas para facilitar a leitura.</summary>
        public static void ApplyZebra(ListView lv)
        {
            for (int i = 0; i < lv.Items.Count; i++)
            {
                lv.Items[i].BackColor = (i % 2 == 0) ? Surface : SurfaceAlt;
                lv.Items[i].ForeColor = Text;
            }
        }

        public static void StyleTextBox(TextBox tb)
        {
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = SurfaceAlt;
            tb.ForeColor = Text;
            tb.Font = new Font(FontFamily, 9.75F);
        }

        // ---------- Menu da bandeja ----------

        public static void StyleContextMenu(ContextMenuStrip menu)
        {
            menu.Renderer = new MenuRenderer();
            menu.Font = new Font(FontFamily, 9.5F);
            menu.ShowImageMargin = true;
            menu.Padding = new Padding(2, 4, 2, 4);
        }

        private class MenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return AccentSoft; } }
            public override Color MenuItemSelectedGradientBegin { get { return AccentSoft; } }
            public override Color MenuItemSelectedGradientEnd { get { return AccentSoft; } }
            public override Color MenuItemBorder { get { return AccentSoft; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Surface; } }
            public override Color CheckBackground { get { return AccentSoft; } }
            public override Color CheckSelectedBackground { get { return AccentSoft; } }
            public override Color CheckPressedBackground { get { return AccentSoft; } }
        }

        private class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuColors())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item.Enabled)
                    e.TextColor = e.Item.Selected ? Accent : Text;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                // Marcador de seleção: círculo laranja
                Rectangle r = e.ImageRectangle;
                int d = Math.Min(r.Width, r.Height) - 8;
                if (d < 6) d = 6;
                Rectangle dot = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Accent))
                    e.Graphics.FillEllipse(b, dot);
            }
        }
    }
}
