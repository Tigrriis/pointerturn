using System.Drawing;
using System.Windows.Forms;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The pieces the simplified dialogs (Drive, Draw settings, and the ones to follow) are built
    /// from, so they look alike. Everything sizes to its text, and design-time pixel sizes are
    /// scaled by AutoScaleMode.Font, so a layout holds at any display scaling.
    /// </summary>
    internal static class DialogKit
    {
        /// <summary>The brand accent and ink (brand/tokens.css): what is selected, and its text.</summary>
        public static readonly Color Accent = Color.FromArgb(0x3F, 0xDC, 0xFF);

        public static readonly Color Ink = Color.FromArgb(0x11, 0x1A, 0x22);

        public static readonly Color Line = Color.FromArgb(0xD5, 0xD9, 0xDC);

        /// <summary>Something to be careful with: its text, and the fill behind it.</summary>
        public static readonly Color Warning = Color.Firebrick;

        public static readonly Color WarningFill = Color.FromArgb(0xFF, 0xDD, 0xDD);

        /// <summary>A fixed dialog, centred, that sizes to what is in it.</summary>
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
            form.Padding = new Padding(12);
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

        public static GroupBox Group(string title, Control content)
        {
            content.Dock = DockStyle.Fill;
            var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 6), Margin = new Padding(0, 0, 0, 10) };
            group.Controls.Add(content);
            return group;
        }

        public static Label Caption(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 8, 0) };

        /// <summary>A grey note that wraps at the given design width.</summary>
        public static Label Note(string text, int width) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = SystemColors.GrayText,
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
            MinimumSize = new Size(80, 27),
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(6, 0, 0, 0),
        };
    }
}
