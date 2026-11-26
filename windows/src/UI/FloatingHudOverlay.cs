using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int ULW_ALPHA = 0x00000002;

        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private System.Windows.Forms.Timer _hideTimer;
        private System.Windows.Forms.Timer _animTimer;
        private HudState _state = HudState.HIDDEN;
        private Func<float> _audioLevelProvider;

        private float _phase = 0f;
        private float _spinnerAngle = 0f;
        private float _smoothedAudioLevel = 0f;

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
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private const int RecordingWidth = 104;
        private const int RecordingHeight = 32;

        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(120, 40);

            _hideTimer = new System.Windows.Forms.Timer();
            _hideTimer.Interval = 1200;
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                _animTimer.Stop();
                _state = HudState.HIDDEN;
                HideWindow();
            };

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 25; // ~40 FPS smooth animation
            _animTimer.Tick += (s, e) =>
            {
                if (_state == HudState.RECORDING)
                {
                    _phase += 0.22f;
                    float raw = _audioLevelProvider != null ? _audioLevelProvider() : 0f;
                    _smoothedAudioLevel = (_smoothedAudioLevel * 0.65f) + (raw * 0.35f);
                    RenderCurrentState();
                }
                else if (_state == HudState.PROCESSING)
                {
                    _spinnerAngle = (_spinnerAngle + 12f) % 360f;
                    RenderCurrentState();
                }
            };
        }

        public void ShowRecording()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _hideTimer.Stop();
                _state = HudState.RECORDING;
                PositionAtScreenCenter(RecordingWidth, RecordingHeight);
                ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
                EnsureTopMost();
                _animTimer.Start();
                RenderCurrentState();
            });
        }

        public void ShowProcessing()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _hideTimer.Stop();
                _state = HudState.PROCESSING;
                PositionAtScreenCenter(40, 40);
                ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
                EnsureTopMost();
                _animTimer.Start();
                RenderCurrentState();
            });
        }

        public void ShowSuccess()
        {
            if (!SettingsService.Current.ShowFloatingHud) return;

            this.InvokeIfRequired(() =>
            {
                _animTimer.Stop();
                _state = HudState.SUCCESS;
                PositionAtScreenCenter(40, 40);
                ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
                EnsureTopMost();
                RenderCurrentState();

                _hideTimer.Interval = 1200;
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
                PositionAtScreenCenter(40, 40);
                ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
                EnsureTopMost();
                RenderCurrentState();

                _hideTimer.Interval = 1800;
                _hideTimer.Start();
            });
        }

        public new void Hide()
        {
            this.InvokeIfRequired(() =>
            {
                _animTimer.Stop();
                _hideTimer.Stop();
                _state = HudState.HIDDEN;
                HideWindow();
            });
        }

        private void HideWindow()
        {
            try
            {
                ShowWindow(this.Handle, SW_HIDE);
            }
            catch { }
        }

        private void EnsureTopMost()
        {
            try
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, this.Left, this.Top, this.Width, this.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private void PositionAtScreenCenter(int w, int h)
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int x = workingArea.Left + (workingArea.Width - w) / 2;
            int y = workingArea.Bottom - h - 65;
            this.SetBounds(x, y, w, h);
        }

        private void RenderCurrentState()
        {
            if (_state == HudState.HIDDEN || !this.IsHandleCreated)
                return;

            int w = this.Width;
            int h = this.Height;
            if (w <= 0 || h <= 0) return;

            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.Transparent);

                    if (_state == HudState.RECORDING)
                    {
                        DrawRecordingPill(g, w, h);
                        DrawWaveformEqualizer(g, w, h);
                    }
                    else if (_state == HudState.PROCESSING)
                    {
                        DrawSpinnerButton(g, w, h);
                    }
                    else if (_state == HudState.SUCCESS)
                    {
                        DrawSuccessButton(g, w, h);
                    }
                    else if (_state == HudState.ERROR)
                    {
                        DrawErrorButton(g, w, h);
                    }
                }

                RenderLayered(bmp);
            }
        }

        private void RenderLayered(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                SIZE size = new SIZE(bmp.Width, bmp.Height);
                POINT pointSource = new POINT(0, 0);
                POINT topPos = new POINT(this.Left, this.Top);
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(this.Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                    DeleteObject(hBitmap);
                }
                DeleteDC(memDc);
            }
        }

        /// <summary>Cápsula preta arredondada de fundo da gravação, como o balão do Android.</summary>
        private void DrawRecordingPill(Graphics g, int w, int h)
        {
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0, 0, w, h), h / 2f))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(240, 14, 15, 18)))
                g.FillPath(bg, path);
        }

        private void DrawWaveformEqualizer(Graphics g, int w, int h)
        {
            float centerY = h / 2f;
            int numBars = 9;
            float margin = 14f;
            float availableWidth = w - (margin * 2f);
            float step = availableWidth / (numBars - 1);

            for (int i = 0; i < numBars; i++)
            {
                float t = (float)i / (numBars - 1); // 0.0 to 1.0

                // Envelope: smooth tapering on left/right ends
                float env = (float)Math.Sin(t * Math.PI);
                float envelope = (float)Math.Pow(env, 0.40);

                // Multi-frequency harmonic motion giving natural acoustic peak/valley wave pattern
                float h1 = (float)Math.Sin(t * 12.0f + _phase * 0.45f);
                float h2 = (float)Math.Cos(t * 24.0f - _phase * 0.70f);
                float h3 = (float)Math.Sin(t * 36.0f + _phase * 1.05f);
                float rawWave = Math.Abs(h1 * 0.45f + h2 * 0.35f + h3 * 0.20f);

                // Altura em repouso: pequena ondulação; com voz, chega quase à altura total da cápsula
                float minHalfH = 2.0f + (rawWave * 2.0f * envelope);
                float voiceHalfH = _smoothedAudioLevel * 11.0f * (0.35f + rawWave * 0.65f) * envelope;

                float halfH = minHalfH + voiceHalfH;
                halfH = Math.Min(halfH, (h / 2.0f) - 4.0f);

                float x = margin + (i * step);
                float yTop = centerY - halfH;
                float yBottom = centerY + halfH;

                // Barra laranja arredondada, na cor do logotipo
                using (Pen barPen = new Pen(Theme.SidebarIconActive, 3f))
                {
                    barPen.StartCap = LineCap.Round;
                    barPen.EndCap = LineCap.Round;
                    g.DrawLine(barPen, x, yTop, x, yBottom);
                }
            }
        }

        private void DrawSpinnerButton(Graphics g, int w, int h)
        {
            int cx = w / 2;
            int cy = h / 2;

            // Circular button background
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(28, 30, 36)))
            {
                g.FillEllipse(bgBrush, cx - 15, cy - 15, 30, 30);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(60, 64, 76), 1.2f))
            {
                g.DrawEllipse(borderPen, cx - 15, cy - 15, 30, 30);
            }

            // Circular track
            RectangleF arcRect = new RectangleF(cx - 9.5f, cy - 9.5f, 19f, 19f);
            using (Pen trackPen = new Pen(Color.FromArgb(50, 55, 68), 2.2f))
            {
                g.DrawEllipse(trackPen, arcRect);
            }

            // Spinning blue arc
            using (Pen spinPen = new Pen(Color.FromArgb(255, 90, 54), 2.4f))
            {
                spinPen.StartCap = LineCap.Round;
                spinPen.EndCap = LineCap.Round;
                g.DrawArc(spinPen, arcRect, _spinnerAngle, 240);
            }
        }

        private void DrawSuccessButton(Graphics g, int w, int h)
        {
            int cx = w / 2;
            int cy = h / 2;

            // Vibrant green circular button
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(34, 197, 94)))
            {
                g.FillEllipse(bgBrush, cx - 15, cy - 15, 30, 30);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(22, 163, 74), 1.2f))
            {
                g.DrawEllipse(borderPen, cx - 15, cy - 15, 30, 30);
            }

            // Crisp white 'V' checkmark
            PointF[] checkPoints = new PointF[]
            {
                new PointF(cx - 5.5f, cy + 0.5f),
                new PointF(cx - 1.5f, cy + 4.5f),
                new PointF(cx + 6.0f, cy - 3.5f)
            };

            using (Pen checkPen = new Pen(Color.White, 2.6f))
            {
                checkPen.StartCap = LineCap.Round;
                checkPen.EndCap = LineCap.Round;
                checkPen.LineJoin = LineJoin.Round;
                g.DrawLines(checkPen, checkPoints);
            }
        }

        private void DrawErrorButton(Graphics g, int w, int h)
        {
            int cx = w / 2;
            int cy = h / 2;

            // Red circular button
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(239, 68, 68)))
            {
                g.FillEllipse(bgBrush, cx - 15, cy - 15, 30, 30);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(220, 38, 38), 1.2f))
            {
                g.DrawEllipse(borderPen, cx - 15, cy - 15, 30, 30);
            }

            // Exclamation mark '!'
            using (Pen pen = new Pen(Color.White, 2.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx, cy - 6.5f, cx, cy + 0.5f);
            }
            using (SolidBrush dot = new SolidBrush(Color.White))
            {
                g.FillEllipse(dot, cx - 1.5f, cy + 3.5f, 3f, 3f);
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
