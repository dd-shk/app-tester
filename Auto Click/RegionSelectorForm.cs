using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlowRunner
{
    public sealed class RegionSelectorForm : Form
    {
        private readonly Bitmap _frozen;
        private readonly Bitmap _scaledPreview;
        private readonly bool _ownScaledPreview;
        private readonly Rectangle _vs;

        private bool _drag;
        private Point _startScreen;
        private Point _endScreen;

        private Rectangle _selected;
        public Rectangle Selected => _selected;

        private const int DimTextHeight = 20;

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

            // Pre-scale once for fast repeated painting; only allocate a new bitmap when
            // the frozen screenshot dimensions differ from the virtual screen (e.g. DPI scaling).
            if (frozen.Width == virtualScreen.Width && frozen.Height == virtualScreen.Height)
            {
                _scaledPreview = frozen;
                _ownScaledPreview = false;
            }
            else
            {
                _scaledPreview = new Bitmap(virtualScreen.Width, virtualScreen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var sg = Graphics.FromImage(_scaledPreview);
                sg.InterpolationMode = InterpolationMode.Low;
                sg.DrawImage(frozen, 0, 0, virtualScreen.Width, virtualScreen.Height);
                _ownScaledPreview = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownScaledPreview)
                _scaledPreview?.Dispose();
            base.Dispose(disposing);
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

            // Use low-quality settings for speed
            e.Graphics.InterpolationMode = InterpolationMode.Low;
            e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;

            // ✅ تصویر Frozen را به اندازه فرم رسم کن (در DPI مختلف هم align میشه)
            e.Graphics.DrawImageUnscaled(_scaledPreview, 0, 0);

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

                // Show dimensions
                var dimText = $"{rScreen.Width} × {rScreen.Height}";
                using var dimFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                using var dimBrush = new SolidBrush(Color.Lime);
                int tx = rClient.Left;
                int ty = rClient.Bottom + 4;
                if (ty + DimTextHeight > Height) ty = rClient.Top - DimTextHeight;
                e.Graphics.DrawString(dimText, dimFont, dimBrush, tx, ty);
            }
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