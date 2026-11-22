using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// What the simplified dialogs that start from a vehicle have in common (Drive, Turning
    /// template): a button for each vehicle along the top, with its picture, the dialog's own options in the
    /// middle, and Draw settings, Units and GO along the bottom. Every choice is kept as it is
    /// made, so a dialog opens as it was left.
    ///
    /// The dialog edits the conventions it is given: the default vehicle, the drawing units,
    /// (through Draw settings) what is drawn and on which layers, and whatever its own options
    /// stand for. The command builds the manoeuvre from them once the dialog returns OK. What a
    /// manoeuvre needs that isn't asked for stays as turn-paths.json has it: the turn radius is
    /// the vehicle's minimum and the clearances the vehicle's own.
    /// </summary>
    internal abstract class VehicleDialog : Form
    {
        /// <summary>
        /// The vehicles offered, by their name in turn-paths.json, with the text on the button.
        /// Later, once trailers are switched back on: the 19 m articulated vehicle, the A-double
        /// and the B-double, as a second row.
        /// </summary>
        private static readonly (string Vehicle, string Caption)[] Offered =
        {
            ("B85", "B85"),
            ("B99", "B99"),
            ("SRV", "SRV"),
            ("MRV", "MRV / 8.8"),
            ("HRV", "HRV / 12.5"),
        };

        private const int ButtonsPerRow = 5;

        /// <summary>The units in the Units box, and the setting each is saved as.</summary>
        private static readonly (string Setting, DrawingUnits Units)[] UnitChoices =
        {
            ("m", DrawingUnits.Metres),
            ("mm", DrawingUnits.Millimetres),
            ("in", DrawingUnits.Inches),
            ("ft", DrawingUnits.Feet),
        };

        private readonly string _name;
        private readonly DrawingUnits _drawingSays;
        private readonly UnitsWarning _unitsWarning;
        private readonly List<RadioButton> _vehicles = new List<RadioButton>();
        private readonly ComboBox _units;
        private readonly Label _note;
        private bool _loading = true;

        /// <param name="title">The window's title.</param>
        /// <param name="name">What the dialog is called in its messages ("Drive").</param>
        /// <param name="conventions">Edited as choices are made.</param>
        /// <param name="insunits">The drawing's INSUNITS, by name ("Millimeters", "Meters", "Undefined"...).</param>
        /// <param name="unitsWarning">How big the drawing is, and whether its units have been asked about.</param>
        protected VehicleDialog(string title, string name, TurnPathConventions conventions, string? insunits, UnitsWarning unitsWarning)
        {
            _name = name;
            Conventions = conventions;
            _drawingSays = DrawingUnits.FromInsunits(insunits) ?? DrawingUnits.Metres;
            _unitsWarning = unitsWarning;

            DialogKit.Style(this, title);

            _units = new ComboBox { Width = 64, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
            foreach (var (setting, _) in UnitChoices)
            {
                _units.Items.Add(setting);
            }

            Tips.SetToolTip(_units, "What the drawing is drawn in. Vehicle sizes, radii and clearances stay in metres.");

            // Only says anything when there is something to say: doubtful units, or a save that failed.
            _note = DialogKit.Note("", 440);
            _note.Margin = new Padding(3, 8, 3, 0);
            _note.Visible = false;
        }

        protected TurnPathConventions Conventions { get; }

        protected ToolTip Tips { get; } = new ToolTip { AutoPopDelay = 20000 };

        /// <summary>True while the dialog is being filled in from the conventions: nothing is kept yet.</summary>
        protected bool Loading => _loading;

        /// <summary>
        /// Lays the dialog out, with the dialog's own <paramref name="options"/> (already showing
        /// what the conventions have) between the vehicles and the bottom row. Called at the end
        /// of the constructor.
        /// </summary>
        protected void Build(params Control[] options)
        {
            // ---------------------------------------------------------------- vehicle
            TableLayoutPanel vehicles = DialogKit.Table(ButtonsPerRow);
            foreach (var (name, caption) in Offered)
            {
                // The conventions decide what there is: a vehicle they don't have gets no button.
                string? key = Conventions.Vehicles.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (key == null)
                {
                    continue;
                }

                RadioButton button = VehicleButton(caption, key);
                Tips.SetToolTip(button, Describe(Conventions.Vehicles[key]));
                vehicles.Controls.Add(button, _vehicles.Count % ButtonsPerRow, _vehicles.Count / ButtonsPerRow);
                _vehicles.Add(button);
            }

            vehicles.RowCount = (_vehicles.Count + ButtonsPerRow - 1) / ButtonsPerRow;
            vehicles.Margin = new Padding(0, 0, 0, 10);

            // ----------------------------------------------------------------- bottom
            Button drawSettings = DialogKit.Button("Draw settings...");
            drawSettings.Margin = new Padding(3, 0, 12, 0);
            drawSettings.Click += (s, e) => EditDrawSettings();

            Label unitsLabel = DialogKit.Caption("Units:");
            unitsLabel.Margin = new Padding(3, 6, 4, 0);

            Button go = DialogKit.Button("GO");
            go.Font = new Font(Font, FontStyle.Bold);
            go.MinimumSize = new Size(110, 32);
            go.Anchor = AnchorStyles.Right;
            go.DialogResult = DialogResult.OK;
            AcceptButton = go;

            TableLayoutPanel bottom = DialogKit.Table(2);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(DialogKit.Flow(drawSettings, unitsLabel, _units), 0, 0);
            bottom.Controls.Add(go, 1, 0);
            bottom.Dock = DockStyle.Fill;
            bottom.Margin = new Padding(0, 8, 0, 0);

            TableLayoutPanel root = DialogKit.Table(1);
            Label vehicleCaption = DialogKit.Caption("Vehicle:");
            vehicleCaption.Margin = new Padding(3, 0, 3, 6);
            DialogKit.AddRow(root, vehicleCaption);
            DialogKit.AddRow(root, vehicles);
            foreach (Control option in options)
            {
                DialogKit.AddRow(root, option);
            }

            DialogKit.AddRow(root, bottom);
            DialogKit.AddRow(root, _note);
            root.Dock = DockStyle.Fill;
            Controls.Add(root);

            ShowChoices();

            foreach (RadioButton button in _vehicles)
            {
                button.CheckedChanged += (s, e) =>
                {
                    ShowSelected(button);
                    if (button.Checked && !_loading)
                    {
                        Conventions.DefaultVehicle = (string)button.Tag!;
                        Remember();
                    }
                };
            }

            _units.SelectedIndexChanged += (s, e) =>
            {
                Conventions.DrawingUnits = SelectedSetting;
                ShowUnitsDoubt();
                Remember();
            };

            // Esc leaves without going ahead (there is no Cancel button to take it).
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && !_units.DroppedDown)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK)
                {
                    return;
                }

                string? problem = _vehicles.All(b => !b.Checked) ? "Choose a vehicle." : Problem();
                if (problem != null)
                {
                    MessageBox.Show(this, problem, _name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
                else if (!_unitsWarning.CarryOn(this, SelectedUnits))
                {
                    // Asked once for each drawing file; the note in red stays either way.
                    e.Cancel = true;
                }
                else
                {
                    // What goes ahead is what is remembered, changed here or not.
                    Remember();
                }
            };

            _loading = false;
        }

        /// <summary>What stops GO (something typed that can't be used), or null.</summary>
        protected virtual string? Problem() => null;

        /// <summary>Keeps this dialog's choices, as the conventions now have them, for next time.</summary>
        protected abstract void Save();

        /// <summary>Keeps the choices made here for next time. Call when one changes.</summary>
        protected void Remember()
        {
            if (!_loading)
            {
                Kept(Save);
            }
        }

        /// <summary>Shows the conventions' vehicle and units.</summary>
        private void ShowChoices()
        {
            // The last vehicle; when that isn't one offered here, the B99 (or the first there is).
            RadioButton? chosen = _vehicles.FirstOrDefault(b => string.Equals((string)b.Tag!, Conventions.DefaultVehicle, StringComparison.OrdinalIgnoreCase))
                ?? _vehicles.FirstOrDefault(b => string.Equals((string)b.Tag!, "B99", StringComparison.OrdinalIgnoreCase))
                ?? _vehicles.FirstOrDefault();
            if (chosen != null)
            {
                chosen.Checked = true;
                Conventions.DefaultVehicle = (string)chosen.Tag!;
            }

            foreach (RadioButton button in _vehicles)
            {
                ShowSelected(button);
            }

            // "auto" follows the drawing, and stays "auto" until another unit is chosen. A drawing in
            // something not on the list (centimetres, say) gets an entry of its own.
            string setting;
            try
            {
                setting = DrawingUnits.Setting(Conventions.DrawingUnits);
            }
            catch (ArgumentException)
            {
                setting = "auto";
            }

            int index = setting == "auto"
                ? Array.FindIndex(UnitChoices, c => Math.Abs(c.Units.PerMetre - _drawingSays.PerMetre) <= 1e-9 * c.Units.PerMetre)
                : Array.FindIndex(UnitChoices, c => c.Setting == setting);
            if (index < 0)
            {
                index = _units.Items.Add(_drawingSays.Name);
                _units.Width = 130;
            }

            _units.SelectedIndex = index;
            ShowUnitsDoubt();
        }

        private DrawingUnits SelectedUnits =>
            _units.SelectedIndex >= 0 && _units.SelectedIndex < UnitChoices.Length ? UnitChoices[_units.SelectedIndex].Units : _drawingSays;

        private string SelectedSetting =>
            _units.SelectedIndex >= 0 && _units.SelectedIndex < UnitChoices.Length ? UnitChoices[_units.SelectedIndex].Setting : "auto";

        private void EditDrawSettings()
        {
            using (var dialog = new DrawSettingsDialog(Conventions))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                // Only the layers changed here are kept: the others go on following turn-paths.json.
                TurnPathLayers layers = dialog.Layers;
                Dictionary<string, string> before = Conventions.Layers.All().ToDictionary(kv => kv.Key, kv => kv.Value);
                List<KeyValuePair<string, string>> changed = layers.All().Where(kv => before[kv.Key] != kv.Value).ToList();
                Conventions.Draw = dialog.Draw;
                Conventions.Layers = layers;
                Kept(() => PointerPathSettings.RememberDrawSettings(Conventions.Draw, changed));
            }
        }

        /// <summary>Runs a save of something chosen here, and says so in the dialog if it fails.</summary>
        protected void Kept(Action save)
        {
            try
            {
                save();
                ShowUnitsDoubt();
            }
            catch (Exception ex)
            {
                // The choice still stands for this run.
                Say("These choices couldn't be kept for next time: " + ex.Message);
            }
        }

        /// <summary>Warns, in red, when the drawing's size makes the units unlikely.</summary>
        private void ShowUnitsDoubt() => Say(_unitsWarning.Doubt(SelectedUnits));

        private void Say(string? warning)
        {
            _note.Text = warning ?? "";
            _note.ForeColor = DialogKit.Warning;
            _note.Visible = !string.IsNullOrEmpty(warning);
        }

        /// <summary>
        /// A vehicle's button: a radio button that looks like a push button and stays down, with a
        /// picture of the vehicle over its name.
        /// </summary>
        private RadioButton VehicleButton(string caption, string vehicle)
        {
            var button = new RadioButton
            {
                Text = caption,
                Tag = vehicle,
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(88, 50),
                Margin = new Padding(3, 0, 5, 6),
                Font = new Font(Font, FontStyle.Bold),
                BackColor = SystemColors.Window,
                ForeColor = DialogKit.Ink,
            };

            Image? icon = VehicleIcon(vehicle);
            if (icon != null)
            {
                // The picture against the top and the name against the bottom, with room round both.
                button.Image = icon;
                button.ImageAlign = ContentAlignment.TopCenter;
                button.TextAlign = ContentAlignment.BottomCenter;
                button.Padding = new Padding(0, 8, 0, 6);
                button.Height = 72;
            }

            return button;
        }

        /// <summary>The icon's size at 100% display scaling; the PNGs are four times this.</summary>
        private static readonly Size IconSize = new Size(76, 26);

        /// <summary>
        /// The vehicle's side view (tools/icons/make_vehicle_icons.py draws them), at the size for
        /// this display. Null for a vehicle without one: its button is just its name.
        /// </summary>
        private Image? VehicleIcon(string vehicle)
        {
            try
            {
                Stream? stream = typeof(VehicleDialog).Assembly.GetManifestResourceStream($"PointerPath.BricsCAD.Resources.vehicle_{vehicle.ToLowerInvariant()}.png");
                if (stream == null)
                {
                    return null;
                }

                using (stream)
                using (Image full = Image.FromStream(stream))
                {
                    // The same factor the dialog's controls are scaled by: the font's size against the design's.
                    float scale = Math.Max(1f, CurrentAutoScaleDimensions.Height / 15f);
                    var icon = new Bitmap((int)Math.Round(IconSize.Width * scale), (int)Math.Round(IconSize.Height * scale));
                    using (Graphics g = Graphics.FromImage(icon))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(full, new Rectangle(0, 0, icon.Width, icon.Height));
                    }

                    return icon;
                }
            }
            catch (Exception)
            {
                return null; // the name alone
            }
        }

        /// <summary>The vehicle chosen is the one in the accent colour, with the heavier border.</summary>
        private static void ShowSelected(RadioButton button)
        {
            button.BackColor = button.Checked ? DialogKit.Accent : SystemColors.Window;
            button.FlatAppearance.CheckedBackColor = DialogKit.Accent;
            button.FlatAppearance.BorderColor = button.Checked ? DialogKit.Ink : DialogKit.Line;
            button.FlatAppearance.BorderSize = button.Checked ? 2 : 1;
        }

        private static string Describe(DesignVehicle v) =>
            $"{v.Length:0.##} x {v.Width:0.##} m, wheelbase {v.Wheelbase:0.##} m, turns at its minimum radius of {v.MinTurnRadius:0.#} m. {v.Description}".TrimEnd();
    }
}
