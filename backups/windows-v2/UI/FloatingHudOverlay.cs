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
        private System.Windows.Forms.Timer _animTimer;
        private HudState _state = HudState.HIDDEN;
        private Func<float> _audioLevelProvider;
        private float _displayAudioLevel = 0f;
        private float _pulsePhase = 0f;
        private string _statusText = "";

        public FloatingHudOverlay(Func<float> audioLevelProvider = null)
        {
            _audioLevelProvider = audioLevelProvider;
            InitializeComponent();
        }

        public void SetAudioLevelProvider(Func<float> provider)
        {
            _audioLevelProvider = provider;
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
            this.Size = new Size(84, 42);
            this.BackColor = Color.FromArgb(18, 18, 22);
            this.DoubleBuffered = true;

            _hideTimer = new System.Windows.Forms.Timer();
            _hideTimer.Interval = 1800;
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                _animTimer.Stop();
                _state = HudState.HIDDEN;
                this.Hide();
            };

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 33; // ~30 FPS smooth rendering
            _animTimer.Tick += AnimTimer_Tick;
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            if (_state == HudState.RECORDING)
            {
                _pulsePhase += 0.12f;
                if (_pulsePhase > (float)(Math.PI * 2))
                    _pulsePhase -= (float)(Math.PI * 2);

                float target = 0f;
                if (_audioLevelProvider != null)
                {
                    try { target = _audioLevelProvider(); } catch { }
                }

                // Smooth interpolation for equalizer bars
                if (target > _displayAudioLevel)
                    _displayAudioLevel = _displayAudioLevel * 0.4f + target * 0.6f;
                else
                    _displayAudioLevel = _displayAudioLevel * 0.8f + target * 0.2f;

                this.Invalidate();
            }
            else if (_state == HudState.PROCESSING)
            {
                _pulsePhase += 0.15f;
                if (_pulsePhase > (float)(Math.PI * 2))
                    _pulsePhase -= (float)(Math.PI * 2);

                this.Invalidate();
            }
        }

        public void ShowRecording()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _hideTimer.Stop();
                _state = HudState.RECORDING;
                _statusText = "";
                this.Size = new Size(84, 42);
                PositionAtBottomRight();
                this.Show();
                EnsureTopMost();
                _animTimer.Start();
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
                _statusText = "⏳";
                this.Size = new Size(52, 42);
                PositionAtBottomRight();
                this.Show();
                EnsureTopMost();
                _animTimer.Start();
                this.Invalidate();
            });
        }

        public void ShowSuccess()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _animTimer.Stop();
                _state = HudState.SUCCESS;
                _statusText = "✓";
                this.Size = new Size(52, 42);
                PositionAtBottomRight();
                this.Show();
                EnsureTopMost();
                this.Invalidate();
                _hideTimer.Start();
            });
        }

        public void ShowError(string msg)
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _animTimer.Stop();
                _state = HudState.ERROR;
                _statusText = "⚠";
                this.Size = new Size(52, 42);
                PositionAtBottomRight();
                this.Show();
                EnsureTopMost();
                this.Invalidate();
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

        private void PositionAtBottomRight()
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Bottom - this.Height - 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            // Draw pill background with subtle glow border
            using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(1, 1, w - 3, h - 3), (h - 3) / 2))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(20, 20, 25)))
                {
                    g.FillPath(bgBrush, path);
                }

                Color borderColor = Color.FromArgb(70, 70, 85);
                if (_state == HudState.RECORDING)
                    borderColor = Color.FromArgb(220, 53, 69);
                else if (_state == HudState.PROCESSING)
                    borderColor = Color.FromArgb(255, 193, 7);
                else if (_state == HudState.SUCCESS)
                    borderColor = Color.FromArgb(40, 167, 69);
                else if (_state == HudState.ERROR)
                    borderColor = Color.FromArgb(220, 53, 69);

                using (Pen borderPen = new Pen(borderColor, 1.8f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            if (_state == HudState.RECORDING)
            {
                // 1. Draw glowing pulsating red dot on left
                float pulse = (float)(Math.Sin(_pulsePhase) + 1.0) / 2.0f; // 0.0 to 1.0
                int dotX = 16;
                int dotY = h / 2;

                // Pulsing outer aura
                int auraSize = (int)(14 + pulse * 6);
                int alpha = (int)(40 + pulse * 60);
                using (SolidBrush auraBrush = new SolidBrush(Color.FromArgb(alpha, 255, 60, 70)))
                {
                    g.FillEllipse(auraBrush, dotX - auraSize / 2, dotY - auraSize / 2, auraSize, auraSize);
                }

                // Core red dot
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(255, 45, 60)))
                {
                    g.FillEllipse(dotBrush, dotX - 4, dotY - 4, 8, 8);
                }

                // 2. Draw dynamic real-time Audio Waveform / VU Bars on right (4 bars)
                int barCount = 4;
                int barWidth = 3;
                int barSpacing = 5;
                int startX = 35;
                int centerY = h / 2;
                int maxBarHeight = 22;
                int minBarHeight = 3;

                // Phase offsets so bars dance fluidly
                float[] multipliers = new float[] { 0.85f, 1.25f, 1.05f, 0.75f };

                for (int i = 0; i < barCount; i++)
                {
                    float barLevel = Math.Min(1.0f, _displayAudioLevel * multipliers[i]);
                    int barH = minBarHeight + (int)((maxBarHeight - minBarHeight) * barLevel);
                    int bx = startX + i * (barWidth + barSpacing);
                    int by = centerY - barH / 2;

                    Color barColor = Color.FromArgb(240, 240, 245);
                    if (barLevel > 0.05f)
                    {
                        // Accent tint on louder sounds
                        barColor = Color.FromArgb(255, 90, 100);
                    }

                    using (SolidBrush barBrush = new SolidBrush(barColor))
                    {
                        // Draw rounded bar
                        FillRoundedRect(g, barBrush, new Rectangle(bx, by, barWidth, barH), barWidth / 2);
                    }
                }
            }
            else
            {
                // Draw Icon centered for PROCESSING, SUCCESS, ERROR
                Color textColor = Color.White;
                if (_state == HudState.PROCESSING) textColor = Color.FromArgb(255, 193, 7);
                else if (_state == HudState.SUCCESS) textColor = Color.FromArgb(40, 167, 69);
                else if (_state == HudState.ERROR) textColor = Color.FromArgb(220, 53, 69);

                using (Font font = new Font("Segoe UI Emoji", 14F, FontStyle.Regular))
                using (SolidBrush brush = new SolidBrush(textColor))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(_statusText, font, brush, new RectangleF(0, 0, w, h), sf);
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

        private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            if (radius <= 0 || rect.Height <= radius * 2 || rect.Width <= radius * 2)
            {
                g.FillRectangle(brush, rect);
                return;
            }

            using (GraphicsPath path = CreateRoundedRectanglePath(rect, radius))
            {
                g.FillPath(brush, path);
            }
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
