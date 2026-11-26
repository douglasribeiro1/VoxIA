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
        // Paleta (claro: papel quente + índigo, igual ao protótipo do Claude Design)
        public static readonly Color Background = Color.FromArgb(244, 242, 236);   // #F4F2EC
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);
        public static readonly Color SurfaceAlt = Color.FromArgb(241, 239, 232);   // #F1EFE8
        public static readonly Color Border = Color.FromArgb(228, 225, 216);       // #E4E1D8
        public static readonly Color BorderStrong = Color.FromArgb(213, 209, 198); // #D5D1C6
        public static readonly Color Divider = Color.FromArgb(239, 237, 230);      // #EFEDE6
        public static readonly Color Text = Color.FromArgb(22, 24, 29);            // #16181D
        public static readonly Color TextSecondary = Color.FromArgb(58, 63, 74);   // #3A3F4A
        public static readonly Color TextMuted = Color.FromArgb(94, 99, 110);      // #5E636E
        public static readonly Color Accent = Color.FromArgb(67, 56, 202);         // #4338CA
        public static readonly Color AccentHover = Color.FromArgb(55, 48, 163);    // #3730A3
        public static readonly Color AccentSoft = Color.FromArgb(236, 234, 253);   // #ECEAFD
        public static readonly Color Cream = Color.FromArgb(255, 255, 255);
        public static readonly Color OnAccent = Color.FromArgb(255, 255, 255);
        public static readonly Color Danger = Color.FromArgb(217, 45, 58);         // #D92D3A
        public static readonly Color DangerHover = Color.FromArgb(185, 30, 44);
        public static readonly Color DangerSoft = Color.FromArgb(251, 227, 229);
        public static readonly Color Success = Color.FromArgb(30, 123, 79);        // #1E7B4F
        public static readonly Color SuccessSoft = Color.FromArgb(227, 244, 234);  // #E3F4EA
        public static readonly Color Warning = Color.FromArgb(180, 110, 0);

        // Barra lateral escura
        public static readonly Color Sidebar = Color.FromArgb(22, 24, 29);         // #16181D
        public static readonly Color SidebarActive = Color.FromArgb(42, 45, 54);   // #2A2D36
        public static readonly Color SidebarCard = Color.FromArgb(35, 38, 46);     // #23262E
        public static readonly Color SidebarText = Color.FromArgb(201, 203, 210);  // #C9CBD2
        public static readonly Color SidebarMuted = Color.FromArgb(163, 167, 177); // #A3A7B1
        public static readonly Color SidebarIcon = Color.FromArgb(139, 143, 154);  // #8B8F9A
        public static readonly Color SidebarIconActive = Color.FromArgb(165, 180, 252); // #A5B4FC
        public static readonly Color LiveGreen = Color.FromArgb(74, 222, 128);     // #4ADE80

        // Títulos em destaque
        public static readonly Color Navy = Text;
        public static readonly Color NavyLight = TextMuted;

        // Tipografia
        public const string FontFamily = "Segoe UI";
        public const string SerifFamily = "Segoe UI Semibold";
        public static readonly Font Body = new Font(FontFamily, 9F, FontStyle.Regular);
        public static readonly Font BodyBold = new Font(FontFamily, 9F, FontStyle.Bold);
        public static readonly Font Small = new Font(FontFamily, 8.25F, FontStyle.Regular);
        public static readonly Font Eyebrow = new Font(FontFamily, 7.5F, FontStyle.Bold);
        public static readonly Font Heading = new Font(FontFamily, 13F, FontStyle.Bold);
        public static readonly Font SectionTitle = new Font(FontFamily, 20F, FontStyle.Bold);
        public static readonly Font CardTitle = new Font(FontFamily, 10.5F, FontStyle.Bold);
        public static readonly Font HeaderTitle = new Font(FontFamily, 15F, FontStyle.Bold);
        public static readonly Font HeaderSubtitle = new Font(FontFamily, 8.5F, FontStyle.Regular);
        public static readonly Font StatValue = new Font(FontFamily, 18F, FontStyle.Bold);

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

        /// <summary>Janela redimensionável e maximizável (janela principal).</summary>
        public static void MakeResizable(Form form, Size minimum)
        {
            form.FormBorderStyle = FormBorderStyle.Sizable;
            form.MaximizeBox = true;
            form.MinimizeBox = true;
            form.MinimumSize = minimum;
        }

        /// <summary>Painel com cantos arredondados que acompanha o tamanho (para layouts com Dock).</summary>
        public class RoundPanel : Panel
        {
            public int Radius = 18;
            public Color Fill = Surface;
            public Color Stroke = Border;

            public RoundPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Surface;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color outer = Parent != null ? Parent.BackColor : Background;
                e.Graphics.Clear(outer);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
                {
                    using (SolidBrush b = new SolidBrush(Fill))
                        e.Graphics.FillPath(b, path);
                    if (Stroke != Color.Empty && Stroke.A > 0)
                        using (Pen p = new Pen(Stroke))
                            e.Graphics.DrawPath(p, path);
                }
            }
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

        /// <summary>
        /// Ícone "?" que mostra a explicação ao passar o mouse (ou ao clicar). Deixa a tela limpa:
        /// a explicação só aparece para quem quiser.
        /// </summary>
        public static Label CreateHelpIcon(string text, Point location)
        {
            // Cada ícone tem a sua dica, descartada junto com ele: uma dica compartilhada continuava
            // tentando esconder ícones de janelas já fechadas e derrubava o app
            ToolTip tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 150, ReshowDelay = 100, ShowAlways = true };
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
            tip.SetToolTip(icon, text);
            icon.Click += (s, e) => { if (!icon.IsDisposed) tip.Show(text, icon, 24, 0, 15000); };
            icon.Disposed += (s, e) => tip.Dispose();
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
            StyleFlat(b, Surface, SurfaceAlt, Text, BorderStrong);
        }

        public static void StyleDanger(Button b)
        {
            StyleFlat(b, Surface, DangerSoft, Danger, Color.FromArgb(240, 190, 195));
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
