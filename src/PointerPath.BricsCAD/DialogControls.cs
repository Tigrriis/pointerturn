using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The button that carries a dialog forward (GO, OK): the brand's ink with white text and
    /// rounded corners, lighter under the pointer. It draws itself, so it looks the same in
    /// every CAD program and Windows theme; the other buttons are left to Windows.
    /// </summary>
    internal sealed class PrimaryButton : Button
    {
        private bool _hover;
        private bool _down;

        public PrimaryButton(string text)
        {
            Text = text;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(104, 31);
            Padding = new Padding(14, 0, 14, 0);
            Margin = new Padding(6, 0, 0, 0);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var face = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(4, Height / 5);
            Color fill = !Enabled ? DialogKit.Line : _down ? Color.Black : _hover ? DialogKit.Slate : DialogKit.Ink;
            using (GraphicsPath shape = DialogKit.Rounded(face, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, shape);
            }

            if (Focused && ShowFocusCues)
            {
                // Reached with the keyboard: a ring in the accent just inside the edge.
                using (GraphicsPath ring = DialogKit.Rounded(Rectangle.Inflate(face, -3, -3), Math.Max(2, radius - 3)))
                using (var pen = new Pen(DialogKit.Accent, 1.5f))
                {
                    g.DrawPath(pen, ring);
                }
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? Color.White : DialogKit.Graphite,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>
    /// A vehicle to choose: a card with the vehicle's picture, its name and its length, one of a
    /// row of which one is chosen (it is a radio button). The one chosen is filled with the pale
    /// accent and edged with the accent. It draws itself, to fit its size on the display.
    /// </summary>
    internal sealed class VehicleCard : RadioButton
    {
        private readonly string _name;
        private readonly string _detail;
        private readonly Image? _icon;
        private bool _hover;

        /// <param name="name">The vehicle's name, as shown ("B99").</param>
        /// <param name="detail">A line under the name ("5.2 m").</param>
        /// <param name="icon">The vehicle's picture, larger than it is shown; null for none.</param>
        public VehicleCard(string name, string detail, Image? icon)
        {
            _name = name;
            _detail = detail;
            _icon = icon;
            Text = "";
            AccessibleName = name + ", " + detail;
            Appearance = Appearance.Button;
            AutoSize = false;
            Size = new Size(100, icon != null ? 86 : 54);
            Margin = new Padding(0, 0, 8, 8);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs eventargs)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(eventargs);
        }

        protected override void OnMouseLeave(EventArgs eventargs)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(eventargs);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        /// <summary>The height of a line of text in a font, in pixels on this display.</summary>
        private static int LineHeight(Graphics g, Font font) =>
            Math.Max((int)Math.Ceiling(font.GetHeight(g)), TextRenderer.MeasureText(g, "Ag", font).Height);

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // The card: white, the paper grey under the pointer, the pale accent when chosen.
            float edge = Checked ? 2f : 1f;
            var card = new RectangleF(edge / 2, edge / 2, Width - edge, Height - edge);
            int radius = Math.Max(5, Height / 10);
            using (GraphicsPath shape = DialogKit.Rounded(card, radius))
            using (var fill = new SolidBrush(Checked ? DialogKit.Highlight : _hover ? DialogKit.Paper : Color.White))
            using (var pen = new Pen(Checked ? DialogKit.Accent : _hover ? DialogKit.Graphite : DialogKit.Line, edge))
            {
                g.FillPath(fill, shape);
                g.DrawPath(pen, shape);
            }

            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = DialogKit.Rounded(RectangleF.Inflate(card, -3.5f, -3.5f), Math.Max(2, radius - 3)))
                using (var pen = new Pen(DialogKit.Graphite, 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawPath(pen, ring);
                }
            }

            // From the bottom up: the length, the name, and the picture in what is left.
            const TextFormatFlags Centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int pad = Math.Max(5, Height / 11);
            Color ink = Enabled ? DialogKit.Ink : DialogKit.Graphite;
            using (var bold = new Font(Font, FontStyle.Bold))
            using (var small = new Font(Font.FontFamily, Font.Size * 0.9f, FontStyle.Regular))
            {
                // Each line gets its font's full line height. (Measuring the text with the centring
                // flags doesn't give it: asked for a single line centred in no height at all, Windows
                // answers with next to none, and the two lines land on top of each other.)
                int detailHeight = LineHeight(g, small), nameHeight = LineHeight(g, bold);
                var detailAt = new Rectangle(0, Height - pad - detailHeight, Width, detailHeight);
                var nameAt = new Rectangle(0, detailAt.Top - nameHeight, Width, nameHeight);
                TextRenderer.DrawText(g, _detail, small, detailAt, DialogKit.Graphite, Centred);
                TextRenderer.DrawText(g, _name, bold, nameAt, ink, Centred);

                if (_icon == null)
                {
                    return;
                }

                Rectangle room = Rectangle.FromLTRB(pad, pad, Width - pad, nameAt.Top - pad / 2);
                if (room.Width <= 0 || room.Height <= 0)
                {
                    return;
                }

                float scale = Math.Min((float)room.Width / _icon.Width, (float)room.Height / _icon.Height);
                float width = _icon.Width * scale, height = _icon.Height * scale;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_icon, room.Left + (room.Width - width) / 2, room.Top + (room.Height - height) / 2, width, height);
            }
        }
    }
}
