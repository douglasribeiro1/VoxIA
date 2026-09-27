using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoxIA.UI
{
    /// <summary>
    /// Identidade visual única do VoxIA: paleta sóbria (azul-marinho + azul-petróleo), tipografia Segoe UI
    /// e componentes reutilizáveis (cabeçalho, rodapé, cartões, botões, listas e menu da bandeja).
    /// </summary>
    public static class Theme
    {
        // Paleta
        public static readonly Color Navy = Color.FromArgb(18, 44, 82);
        public static readonly Color NavyLight = Color.FromArgb(156, 178, 208);
        public static readonly Color Accent = Color.FromArgb(11, 114, 168);
        public static readonly Color AccentHover = Color.FromArgb(8, 94, 140);
        public static readonly Color AccentSoft = Color.FromArgb(230, 242, 249);
        public static readonly Color Background = Color.FromArgb(243, 245, 248);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
        public static readonly Color Border = Color.FromArgb(218, 224, 232);
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color TextMuted = Color.FromArgb(100, 112, 128);
        public static readonly Color Danger = Color.FromArgb(185, 45, 45);
        public static readonly Color DangerHover = Color.FromArgb(155, 32, 32);
        public static readonly Color Success = Color.FromArgb(25, 135, 84);
        public static readonly Color Warning = Color.FromArgb(196, 110, 0);

        // Tipografia
        public const string FontFamily = "Segoe UI";
        public static readonly Font Body = new Font(FontFamily, 9F, FontStyle.Regular);
        public static readonly Font BodyBold = new Font(FontFamily, 9F, FontStyle.Bold);
        public static readonly Font Small = new Font(FontFamily, 8.25F, FontStyle.Regular);
        public static readonly Font Heading = new Font(FontFamily, 13F, FontStyle.Bold);
        public static readonly Font SectionTitle = new Font(FontFamily, 12F, FontStyle.Bold);
        public static readonly Font CardTitle = new Font(FontFamily, 9.75F, FontStyle.Bold);
        public static readonly Font HeaderTitle = new Font(FontFamily, 14F, FontStyle.Bold);
        public static readonly Font HeaderSubtitle = new Font(FontFamily, 9F, FontStyle.Regular);
        public static readonly Font StatValue = new Font(FontFamily, 16F, FontStyle.Bold);

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

        /// <summary>Faixa superior azul-marinho com título e subtítulo.</summary>
        public static Panel CreateHeader(string title, string subtitle)
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Navy };

            Panel accentBar = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Accent };

            Label lblTitle = new Label
            {
                Text = title,
                Font = HeaderTitle,
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(22, 11),
                BackColor = Color.Transparent
            };
            Label lblSubtitle = new Label
            {
                Text = subtitle,
                Font = HeaderSubtitle,
                ForeColor = NavyLight,
                AutoSize = true,
                Location = new Point(24, 37),
                BackColor = Color.Transparent
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSubtitle);
            header.Controls.Add(accentBar);
            return header;
        }

        /// <summary>Rodapé branco com linha divisória superior, para os botões de ação.</summary>
        public static Panel CreateFooter()
        {
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = FooterHeight, BackColor = Surface };
            footer.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Border))
                    e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            return footer;
        }

        /// <summary>Cartão branco com borda sutil.</summary>
        public static Panel CreateCard(Rectangle bounds)
        {
            Panel card = new Panel { Bounds = bounds, BackColor = Surface };
            card.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Border))
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };
            return card;
        }

        public static Label CreateCardTitle(string text, Point location)
        {
            return new Label { Text = text, Font = CardTitle, ForeColor = Text, AutoSize = true, Location = location, BackColor = Color.Transparent };
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

        // ---------- Botões ----------

        public static void StylePrimary(Button b)
        {
            StyleFlat(b, Accent, AccentHover, Color.White, Accent);
            b.Font = BodyBold;
        }

        public static void StyleSecondary(Button b)
        {
            StyleFlat(b, Surface, SurfaceAlt, Text, Border);
        }

        public static void StyleDanger(Button b)
        {
            StyleFlat(b, Surface, Color.FromArgb(253, 240, 240), Danger, Color.FromArgb(232, 190, 190));
        }

        public static void StyleDangerSolid(Button b)
        {
            StyleFlat(b, Danger, DangerHover, Color.White, Danger);
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
        }

        /// <summary>Linhas zebradas para facilitar a leitura.</summary>
        public static void ApplyZebra(ListView lv)
        {
            for (int i = 0; i < lv.Items.Count; i++)
            {
                lv.Items[i].BackColor = (i % 2 == 0) ? Surface : SurfaceAlt;
            }
        }

        public static void StyleTextBox(TextBox tb)
        {
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = Surface;
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
                    e.TextColor = e.Item.Selected ? Navy : Text;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                // Marcador de seleção: círculo azul-petróleo
                Rectangle r = e.ImageRectangle;
                int d = Math.Min(r.Width, r.Height) - 8;
                if (d < 6) d = 6;
                Rectangle dot = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Accent))
                    e.Graphics.FillEllipse(b, dot);
            }
        }
    }
}
