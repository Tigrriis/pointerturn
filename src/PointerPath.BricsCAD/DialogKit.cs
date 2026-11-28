using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The pieces the simplified dialogs (Drive, Turning template, Draw settings, Conflict
    /// settings, and the ones to follow) are built from, so they look alike: white, a heading,
    /// the brand's colours, and a grey strip of buttons along the bottom. Everything sizes to its text, and design-time pixel sizes are
    /// scaled by AutoScaleMode.Font, so a layout holds at any display scaling.
    /// </summary>
    internal static class DialogKit
    {
        /// <summary>The brand accent and ink (brand/tokens.css): what is selected, and its text.</summary>
        public static readonly Color Accent = Color.FromArgb(0x3F, 0xDC, 0xFF);

        public static readonly Color Ink = Color.FromArgb(0x11, 0x1A, 0x22);

        public static readonly Color Line = Color.FromArgb(0xD5, 0xD9, 0xDC);

        /// <summary>The rest of the brand's colours: the page grey, the pale accent, body text and secondary text.</summary>
        public static readonly Color Paper = Color.FromArgb(0xF5, 0xF6, 0xF4);

        public static readonly Color Highlight = Color.FromArgb(0xCB, 0xF5, 0xFF);

        public static readonly Color Slate = Color.FromArgb(0x36, 0x41, 0x4B);

        public static readonly Color Graphite = Color.FromArgb(0x55, 0x60, 0x6B);

        /// <summary>Something needs attention: the fill behind a note that says so.</summary>
        public static readonly Color Caution = Color.FromArgb(0xFF, 0xEF, 0xB0);

        /// <summary>Something to be careful with: its text, and the fill behind it.</summary>
        public static readonly Color Warning = Color.Firebrick;

        public static readonly Color WarningFill = Color.FromArgb(0xFF, 0xDD, 0xDD);

        /// <summary>A fixed dialog, centred, white, that sizes to what is in it. <see cref="Compose"/> fills it.</summary>
        public static void Style(Form form, string title)
        {
            form.Text = title;
            form.Font = new Font("Segoe UI", 9f);
            form.AutoScaleDimensions = new SizeF(7f, 15f);
            form.AutoScaleMode = AutoScaleMode.Font;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            form.Padding = new Padding(0);
            form.BackColor = Color.White;
            form.ForeColor = Slate;
        }

        /// <summary>How far the dialog's content stands in from its sides.</summary>
        public const int Gutter = 18;

        /// <summary>
        /// Fills a dialog: a heading with a line saying what the dialog is for, the content, and a
        /// grey strip along the bottom with the buttons, the ones that carry on at the right.
        /// </summary>
        /// <param name="left">What goes at the left of the bottom strip (settings, Defaults), or null.</param>
        /// <param name="right">The buttons at the right of the bottom strip, in order.</param>
        public static void Compose(Form form, string heading, string hint, Control content, Control? left, params Control[] right)
        {
            TableLayoutPanel head = Table(1);
            head.Padding = new Padding(Gutter, 16, Gutter, 2);
            head.Dock = DockStyle.Fill;
            AddRow(head, new Label { Text = heading, AutoSize = true, Font = HeadingFont(), ForeColor = Ink, Margin = new Padding(0, 0, 0, 3) });
            AddRow(head, new Label { Text = hint, AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Graphite, Margin = new Padding(1, 0, 0, 0) });

            content.Margin = new Padding(Gutter, 14, Gutter, 16);
            content.Dock = DockStyle.Fill;

            TableLayoutPanel foot = Table(2);
            foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foot.BackColor = Paper;
            foot.Padding = new Padding(Gutter, 12, Gutter, 12);
            foot.Dock = DockStyle.Fill;
            foot.Paint += (s, e) =>
            {
                using (var pen = new Pen(Line))
                {
                    e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0);
                }
            };
            if (left != null)
            {
                left.Anchor = AnchorStyles.Left;
                foot.Controls.Add(left, 0, 0);
            }

            FlowLayoutPanel buttons = Flow(right);
            buttons.Anchor = AnchorStyles.Right;
            foot.Controls.Add(buttons, 1, 0);

            TableLayoutPanel root = Table(1);
            AddRow(root, head);
            AddRow(root, content);
            AddRow(root, foot);
            root.Dock = DockStyle.Fill;
            form.Controls.Add(root);
        }

        /// <summary>The heading's font: Segoe UI Semibold where Windows has it, bold otherwise.</summary>
        public static Font HeadingFont()
        {
            var font = new Font("Segoe UI Semibold", 13f);
            if (font.Name.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
            return new Font("Segoe UI", 13f, FontStyle.Bold);
        }

        /// <summary>The title over a group of controls.</summary>
        public static Label Section(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Ink,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 7),
        };

        /// <summary>A note that needs attention: dark text on the pale yellow, as wide as what it is in.</summary>
        public static Label CautionNote(int width) => new Label
        {
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            BackColor = Caution,
            ForeColor = Ink,
            Padding = new Padding(9, 7, 9, 7),
            Margin = new Padding(0, 12, 0, 0),
            Visible = false,
        };

        /// <summary>A rectangle with rounded corners, to fill or outline.</summary>
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>An auto-sizing table whose rows grow to their content.</summary>
        public static TableLayoutPanel Table(int columns) => new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            RowCount = 0,
            Margin = new Padding(0),
        };

        /// <summary>Adds a control on a row of its own, across every column.</summary>
        public static void AddRow(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount;
            table.Controls.Add(control, 0, row);
            if (table.ColumnCount > 1)
            {
                table.SetColumnSpan(control, table.ColumnCount);
            }

            table.RowCount = row + 1;
        }

        /// <summary>Adds one row: caption | control.</summary>
        public static void AddRow(TableLayoutPanel table, Control caption, Control control)
        {
            int row = table.RowCount;
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(control, 1, row);
            table.RowCount = row + 1;
        }

        /// <summary>Controls side by side, sized to fit.</summary>
        public static FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            flow.Controls.AddRange(controls);
            return flow;
        }

        public static Label Caption(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 8, 0) };

        /// <summary>A grey note that wraps at the given design width.</summary>
        public static Label Note(string text, int width) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = Graphite,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 2, 3, 6),
        };

        public static TextBox Box(int width) => new TextBox { Width = width, Anchor = AnchorStyles.Left, Margin = new Padding(3, 2, 3, 2) };

        public static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 2, 12, 2) };

        public static Button Button(string text) => new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(84, 31),
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(6, 0, 0, 0),
        };
    }
}
