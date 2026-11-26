using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxIA.UI
{
    /// <summary>
    /// Interruptor liga/desliga no estilo do VoxIA. Herda de CheckBox, então Checked, CheckedChanged
    /// e o clique (ou a barra de espaço) funcionam como antes; só o desenho muda.
    /// </summary>
    public class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = "";
            AutoSize = false;
            Size = new Size(44, 24);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // O fundo é pintado em OnPaint, junto com o interruptor
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back = Parent != null ? Parent.BackColor : Theme.Surface;
            g.Clear(back);

            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            Color trackColor = Checked ? Theme.Accent : Theme.BorderStrong;
            if (!Enabled) trackColor = Theme.SurfaceAlt;

            using (GraphicsPath path = Theme.RoundedRect(track, track.Height / 2f))
            using (SolidBrush b = new SolidBrush(trackColor))
                g.FillPath(b, path);

            int knob = Height - 8;
            int knobX = Checked ? Width - knob - 4 : 4;
            Color knobColor = Checked ? Color.FromArgb(255, 250, 245) : Color.White;
            using (SolidBrush k = new SolidBrush(knobColor))
                g.FillEllipse(k, knobX, 4, knob, knob);
        }
    }
}
