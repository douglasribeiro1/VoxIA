using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA.UI
{
    public enum HudState
    {
        HIDDEN,
        RECORDING,
        PROCESSING,
        SUCCESS,
        ERROR
    }

    public class FloatingHudOverlay : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private System.Windows.Forms.Timer _hideTimer;
        private HudState _state = HudState.HIDDEN;
        private string _statusText = "";

        public FloatingHudOverlay(Func<float> audioLevelProvider = null)
        {
            InitializeComponent();
        }

        public void SetAudioLevelProvider(Func<float> provider)
        {
            // Kept for backward compatibility
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(160, 40);

            // Transparency Key ensures the area outside the rounded pill is 100% transparent (no black rectangle)
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;
            this.DoubleBuffered = true;

            _hideTimer = new System.Windows.Forms.Timer();
            _hideTimer.Interval = 1300;
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                _state = HudState.HIDDEN;
                this.Hide();
            };
        }

        public void ShowRecording()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _hideTimer.Stop();
                _state = HudState.RECORDING;
                _statusText = "Gravando...";
                this.Size = new Size(150, 40);
                PositionAtScreenCenter();
                this.Show();
                EnsureTopMost();
                this.Invalidate();
            });
        }

        public void ShowProcessing()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _hideTimer.Stop();
                _state = HudState.PROCESSING;
                _statusText = "Processando...";
                this.Size = new Size(160, 40);
                PositionAtScreenCenter();
                this.Show();
                EnsureTopMost();
                this.Invalidate();
            });
        }

        public void ShowSuccess()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _state = HudState.SUCCESS;
                _statusText = "Transcrito!";
                this.Size = new Size(140, 40);
                PositionAtScreenCenter();
                this.Show();
                EnsureTopMost();
                this.Invalidate();
                _hideTimer.Interval = 1200;
                _hideTimer.Start();
            });
        }

        public void ShowError(string msg)
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _state = HudState.ERROR;
                _statusText = "Falha no áudio";
                this.Size = new Size(150, 40);
                PositionAtScreenCenter();
                this.Show();
                EnsureTopMost();
                this.Invalidate();
                _hideTimer.Interval = 2000;
                _hideTimer.Start();
            });
        }

        private void EnsureTopMost()
        {
            try
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private void PositionAtScreenCenter()
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int x = workingArea.Left + (workingArea.Width - this.Width) / 2;
            int y = workingArea.Bottom - this.Height - 65;
            this.Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            // Draw pill background with clean border
            using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(2, 2, w - 5, h - 5), (h - 5) / 2))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(22, 22, 28)))
                {
                    g.FillPath(bgBrush, path);
                }

                Color borderColor = Color.FromArgb(70, 70, 85);
                if (_state == HudState.RECORDING)
                    borderColor = Color.FromArgb(235, 55, 70);
                else if (_state == HudState.PROCESSING)
                    borderColor = Color.FromArgb(255, 193, 7);
                else if (_state == HudState.SUCCESS)
                    borderColor = Color.FromArgb(40, 167, 69);
                else if (_state == HudState.ERROR)
                    borderColor = Color.FromArgb(220, 53, 69);

                using (Pen borderPen = new Pen(borderColor, 2.0f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            // Draw icon indicator and text
            if (_state == HudState.RECORDING)
            {
                // Solid red recording indicator
                int dotX = 22;
                int dotY = h / 2;
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(255, 45, 60)))
                {
                    g.FillEllipse(dotBrush, dotX - 6, dotY - 6, 12, 12);
                }

                // Crisp text
                using (Font font = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(_statusText, font, textBrush, new PointF(38, (h - font.Height) / 2 - 1));
                }
            }
            else if (_state == HudState.PROCESSING)
            {
                // Amber hourglass icon + text
                using (Font iconFont = new Font("Segoe UI", 10.5F, FontStyle.Regular))
                using (Font textFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                using (SolidBrush amberBrush = new SolidBrush(Color.FromArgb(255, 193, 7)))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString("⏳", iconFont, amberBrush, new PointF(14, (h - iconFont.Height) / 2));
                    g.DrawString(_statusText, textFont, textBrush, new PointF(38, (h - textFont.Height) / 2 - 1));
                }
            }
            else if (_state == HudState.SUCCESS)
            {
                // Green checkmark + text
                using (Font iconFont = new Font("Segoe UI", 11F, FontStyle.Bold))
                using (Font textFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                using (SolidBrush greenBrush = new SolidBrush(Color.FromArgb(40, 190, 80)))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString("✓", iconFont, greenBrush, new PointF(16, (h - iconFont.Height) / 2));
                    g.DrawString(_statusText, textFont, textBrush, new PointF(36, (h - textFont.Height) / 2 - 1));
                }
            }
            else if (_state == HudState.ERROR)
            {
                // Red warning + text
                using (Font iconFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                using (Font textFont = new Font("Segoe UI", 10F, FontStyle.Bold))
                using (SolidBrush redBrush = new SolidBrush(Color.FromArgb(235, 55, 70)))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString("⚠", iconFont, redBrush, new PointF(14, (h - iconFont.Height) / 2));
                    g.DrawString(_statusText, textFont, textBrush, new PointF(36, (h - textFont.Height) / 2 - 1));
                }
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void InvokeIfRequired(Action action)
        {
            if (this.InvokeRequired)
                this.Invoke(action);
            else
                action();
        }
    }
}
