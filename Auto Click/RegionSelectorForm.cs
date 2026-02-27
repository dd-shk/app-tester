using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlowRunner
{
    public sealed class RegionSelectorForm : Form
    {
        private readonly Bitmap _frozen;
        private readonly Rectangle _vs;

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
            Bounds = _vs; // screen coords
            Cursor = Cursors.Cross;
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

            // ✅ DPI-safe: screen coords واقعی
            _startScreen = Cursor.Position;
            _endScreen = _startScreen;

            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_drag) return;

            // ✅ DPI-safe
            _endScreen = Cursor.Position;

            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_drag || e.Button != MouseButtons.Left) return;

            _drag = false;

            // ✅ DPI-safe
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

            // ✅ تصویر Frozen را به اندازه فرم رسم کن (در DPI مختلف هم align میشه)
            e.Graphics.DrawImage(_frozen, new Rectangle(0, 0, Width, Height));

            // overlay
            using (var overlay = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                e.Graphics.FillRectangle(overlay, new Rectangle(0, 0, Width, Height));

            if (_drag)
            {
                var rScreen = Normalize(_startScreen, _endScreen);

                // screen -> client
                var p1 = PointToClient(new Point(rScreen.Left, rScreen.Top));
                var p2 = PointToClient(new Point(rScreen.Right, rScreen.Bottom));
                var rClient = Normalize(p1, p2);

                using var pen = new Pen(Color.Lime, 2);
                e.Graphics.DrawRectangle(pen, rClient);

                using var inner = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                e.Graphics.FillRectangle(inner, rClient);
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