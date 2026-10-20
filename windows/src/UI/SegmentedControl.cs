using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxIA.UI
{
    /// <summary>
    /// Seletor de opções lado a lado ("Alternar | Segurar"): a opção escolhida fica em destaque creme.
    /// </summary>
    public class SegmentedControl : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _selectedIndex = 0;

        /// <summary>Disparado quando o usuário clica numa opção (não quando o código muda SelectedIndex).</summary>
        public event EventHandler UserSelectionChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(268, 36);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public void SetItems(params string[] items)
        {
            _items.Clear();
            _items.AddRange(items);
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (value < 0 || value >= _items.Count) return;
                _selectedIndex = value;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_items.Count == 0 || !Enabled) return;

            int index = (int)(e.X / (Width / (float)_items.Count));
            if (index < 0) index = 0;
            if (index >= _items.Count) index = _items.Count - 1;

            Focus();
            if (index == _selectedIndex) return;

            _selectedIndex = index;
            Invalidate();
            if (UserSelectionChanged != null) UserSelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // O fundo é pintado em OnPaint
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundedRect(track, track.Height / 2f))
            {
                using (SolidBrush b = new SolidBrush(Theme.SurfaceAlt))
                    g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border))
                    g.DrawPath(p, path);
            }

            if (_items.Count == 0) return;

            float segWidth = Width / (float)_items.Count;
            for (int i = 0; i < _items.Count; i++)
            {
                RectangleF seg = new RectangleF(i * segWidth + 3, 3, segWidth - 6, Height - 7);
                bool selected = (i == _selectedIndex);

                if (selected)
                {
                    using (GraphicsPath path = Theme.RoundedRect(seg, seg.Height / 2f))
                    using (SolidBrush b = new SolidBrush(Theme.Cream))
                        g.FillPath(b, path);
                }

                Rectangle textRect = Rectangle.Round(seg);
                TextRenderer.DrawText(g, _items[i], selected ? Theme.BodyBold : Theme.Body, textRect,
                    selected ? Color.FromArgb(20, 16, 14) : Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            if (Focused)
            {
                Rectangle focus = new Rectangle(2, 2, Width - 5, Height - 5);
                ControlPaint.DrawFocusRectangle(g, focus);
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }
}
