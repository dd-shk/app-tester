using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlowRunner
{
    public sealed class RegionSelectorForm : Form
    {
        private readonly Bitmap _frozen;
        private readonly Rectangle _vs;
        private readonly Bitmap? _scaledPreviewBitmap; // Non-null only when a new scaled bitmap was created
        private Image? _scaledPreview;

        private bool _drag;
        private Point _startScreen;
        private Point _endScreen;

        private Rectangle _selected;
        public Rectangle Selected => _selected;

        private RegionSelectorForm(Bitmap frozen, Rectangle virtualScreen)
        {
            _frozen = frozen;
            _vs = virtualScreen;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;

            StartPosition = FormStartPosition.Manual;
            Bounds = _vs;
            Cursor = Cursors.Cross;

            _scaledPreview = CreateScaledPreview(out _scaledPreviewBitmap);
        }

        private Image CreateScaledPreview(out Bitmap? scaledBitmap)
        {
            scaledBitmap = null;
            if (_frozen.Width > 3840 || _frozen.Height > 2160)
            {
                var scale = 0.5;
                var newWidth = (int)(_frozen.Width * scale);
                var newHeight = (int)(_frozen.Height * scale);

                var scaled = new Bitmap(newWidth, newHeight);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.Low;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_frozen, 0, 0, newWidth, newHeight);
                }
                scaledBitmap = scaled;
                return scaled;
            }

            return _frozen;
        }

        public static Rectangle Pick(Bitmap frozen, Rectangle virtualScreen)
        {
            using var f = new RegionSelectorForm(frozen, virtualScreen);
            var r = f.ShowDialog();
            return r == DialogResult.OK ? f.Selected : Rectangle.Empty;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            _drag = true;
            _startScreen = Cursor.Position;
            _endScreen = _startScreen;

            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_drag) return;

            _endScreen = Cursor.Position;
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_drag || e.Button != MouseButtons.Left) return;

            _drag = false;
            _endScreen = Cursor.Position;
            _selected = Normalize(_startScreen, _endScreen);

            if (_selected.Width < 10 || _selected.Height < 10)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();

            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var imgToDraw = _scaledPreview ?? _frozen;

            e.Graphics.InterpolationMode = InterpolationMode.Low;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;

            e.Graphics.DrawImage(imgToDraw, new Rectangle(0, 0, Width, Height));

            using (var overlay = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                e.Graphics.FillRectangle(overlay, new Rectangle(0, 0, Width, Height));

            if (_drag)
            {
                var rScreen = Normalize(_startScreen, _endScreen);
                var p1 = PointToClient(new Point(rScreen.Left, rScreen.Top));
                var p2 = PointToClient(new Point(rScreen.Right, rScreen.Bottom));
                var rClient = Normalize(p1, p2);

                using var pen = new Pen(Color.Lime, 2);
                e.Graphics.DrawRectangle(pen, rClient);

                using var inner = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                e.Graphics.FillRectangle(inner, rClient);

                var dims = $"{rScreen.Width} x {rScreen.Height}";
                using var font = new Font("Segoe UI", 12f, FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));

                var size = e.Graphics.MeasureString(dims, font);
                var textX = rClient.X + rClient.Width / 2 - size.Width / 2;
                var textY = rClient.Y - 30;

                e.Graphics.FillRectangle(bgBrush, textX - 4, textY - 2, size.Width + 8, size.Height + 4);
                e.Graphics.DrawString(dims, font, brush, textX, textY);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scaledPreviewBitmap?.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int x1 = Math.Min(a.X, b.X);
            int y1 = Math.Min(a.Y, b.Y);
            int x2 = Math.Max(a.X, b.X);
            int y2 = Math.Max(a.Y, b.Y);
            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }
    }
}