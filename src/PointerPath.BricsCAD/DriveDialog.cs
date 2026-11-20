using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The Drive dialog (PTDRIVE): pick the vehicle, tick what applies, GO. Every choice is kept
    /// as it is made, so the dialog opens as it was left and Quick drive (PTQDRIVE) can go
    /// without it.
    ///
    /// The dialog edits the conventions it is given: the default vehicle, the conflict check, the
    /// wheels, the drawing units, and (through Draw settings) what is drawn and on which layers.
    /// The command builds the drive from them once the dialog returns OK. What Drive needs that
    /// isn't asked for here stays as turn-paths.json has it: the turn radius is the vehicle's
    /// minimum and the clearances the vehicle's own.
    /// </summary>
    internal sealed class DriveDialog : Form
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

        private readonly TurnPathConventions _conventions;
        private readonly DrawingUnits _drawingSays;
        private readonly double? _drawingExtent;
        private readonly List<RadioButton> _vehicles = new List<RadioButton>();
        private readonly CheckBox _detect;
        private readonly CheckBox _stationary;
        private readonly ComboBox _units;
        private readonly Label _note;
        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000 };
        private bool _loading = true;

        /// <param name="conventions">Edited as choices are made.</param>
        /// <param name="insunits">The drawing's INSUNITS, by name ("Millimeters", "Meters", "Undefined"...).</param>
        /// <param name="drawingExtent">How big the drawing is (the larger of its width and height, in drawing units), if known.</param>
        public DriveDialog(TurnPathConventions conventions, string? insunits, double? drawingExtent)
        {
            _conventions = conventions;
            _drawingSays = DrawingUnits.FromInsunits(insunits) ?? DrawingUnits.Metres;
            _drawingExtent = drawingExtent;

            DialogKit.Style(this, "PointerPath - Drive");

            // ---------------------------------------------------------------- vehicle
            TableLayoutPanel vehicles = DialogKit.Table(ButtonsPerRow);
            foreach (var (name, caption) in Offered)
            {
                // The conventions decide what there is: a vehicle they don't have gets no button.
                string? key = conventions.Vehicles.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (key == null)
                {
                    continue;
                }

                RadioButton button = VehicleButton(caption, key);
                _tips.SetToolTip(button, Describe(conventions.Vehicles[key]));
                vehicles.Controls.Add(button, _vehicles.Count % ButtonsPerRow, _vehicles.Count / ButtonsPerRow);
                _vehicles.Add(button);
            }

            vehicles.RowCount = (_vehicles.Count + ButtonsPerRow - 1) / ButtonsPerRow;
            vehicles.Margin = new Padding(0, 0, 0, 10);

            // ---------------------------------------------------------------- options
            _detect = DialogKit.Check("Detect conflicts");
            _detect.Margin = new Padding(3, 2, 3, 4);
            _tips.SetToolTip(_detect, conventions.ObstructionLayers.Count > 0
                ? "Flags where the swept path or its clearance meets kerbs, walls and columns on: " + string.Join(", ", conventions.ObstructionLayers) + "."
                : "Flags where the swept path or its clearance meets kerbs, walls and columns. Once the vehicle is driven, you pick objects on the layers to check against.");

            _stationary = DialogKit.Check("Allow turn wheels while stopped");
            _stationary.Margin = new Padding(3, 2, 3, 12);
            _stationary.Padding = new Padding(3, 1, 6, 1);
            _tips.SetToolTip(_stationary, "The wheels are set before each forward move, so every leg is on its turning circle from the first metre: tighter, but it is steering on the spot.");

            // ----------------------------------------------------------------- bottom
            Button drawSettings = DialogKit.Button("Draw settings...");
            drawSettings.Margin = new Padding(3, 0, 12, 0);
            drawSettings.Click += (s, e) => EditDrawSettings();

            Label unitsLabel = DialogKit.Caption("Units:");
            unitsLabel.Margin = new Padding(3, 6, 4, 0);
            _units = new ComboBox { Width = 64, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
            foreach (var (setting, _) in UnitChoices)
            {
                _units.Items.Add(setting);
            }

            _tips.SetToolTip(_units, "What the drawing is drawn in. Vehicle sizes, radii and clearances stay in metres.");

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

            // Only says anything when there is something to say: doubtful units, or a save that failed.
            _note = DialogKit.Note("", 440);
            _note.Margin = new Padding(3, 8, 3, 0);
            _note.Visible = false;

            TableLayoutPanel root = DialogKit.Table(1);
            Label vehicleCaption = DialogKit.Caption("Vehicle:");
            vehicleCaption.Margin = new Padding(3, 0, 3, 6);
            DialogKit.AddRow(root, vehicleCaption);
            DialogKit.AddRow(root, vehicles);
            DialogKit.AddRow(root, _detect);
            DialogKit.AddRow(root, _stationary);
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
                        _conventions.DefaultVehicle = (string)button.Tag!;
                        Remember();
                    }
                };
            }

            _detect.CheckedChanged += (s, e) =>
            {
                _conventions.CheckConflicts = _detect.Checked;
                Remember();
            };
            _stationary.CheckedChanged += (s, e) =>
            {
                ShowStationary();
                _conventions.DriveTurnWheelsWhenStationary = _stationary.Checked;
                Remember();
            };
            _units.SelectedIndexChanged += (s, e) =>
            {
                _conventions.DrawingUnits = SelectedSetting;
                ShowUnitsDoubt();
                Remember();
            };

            // Esc leaves without driving (there is no Cancel button to take it).
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

                if (_vehicles.All(b => !b.Checked))
                {
                    MessageBox.Show(this, "Choose a vehicle.", "Drive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
                else if (SelectedUnits.Doubt(_drawingExtent) is string doubt
                    && MessageBox.Show(this, doubt + "\n\nCarry on in " + SelectedUnits.Name + "?", "Drawing units", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    // The wrong units would draw the vehicle a thousand times too big or too small.
                    e.Cancel = true;
                }
                else
                {
                    // What is driven is what Quick drive does next, changed here or not.
                    Remember();
                }
            };

            _loading = false;
        }

        /// <summary>Shows the conventions' choices.</summary>
        private void ShowChoices()
        {
            // The last vehicle; when that isn't one offered here, the B99 (or the first there is).
            RadioButton? chosen = _vehicles.FirstOrDefault(b => string.Equals((string)b.Tag!, _conventions.DefaultVehicle, StringComparison.OrdinalIgnoreCase))
                ?? _vehicles.FirstOrDefault(b => string.Equals((string)b.Tag!, "B99", StringComparison.OrdinalIgnoreCase))
                ?? _vehicles.FirstOrDefault();
            if (chosen != null)
            {
                chosen.Checked = true;
                _conventions.DefaultVehicle = (string)chosen.Tag!;
            }

            foreach (RadioButton button in _vehicles)
            {
                ShowSelected(button);
            }

            _detect.Checked = _conventions.CheckConflicts;
            _stationary.Checked = _conventions.DriveTurnWheelsWhenStationary;
            ShowStationary();

            // "auto" follows the drawing, and stays "auto" until another unit is chosen. A drawing in
            // something not on the list (centimetres, say) gets an entry of its own.
            string setting;
            try
            {
                setting = DrawingUnits.Setting(_conventions.DrawingUnits);
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
            using (var dialog = new DrawSettingsDialog(_conventions))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                // Only the layers changed here are kept: the others go on following turn-paths.json.
                TurnPathLayers layers = dialog.Layers;
                Dictionary<string, string> before = _conventions.Layers.All().ToDictionary(kv => kv.Key, kv => kv.Value);
                List<KeyValuePair<string, string>> changed = layers.All().Where(kv => before[kv.Key] != kv.Value).ToList();
                _conventions.Draw = dialog.Draw;
                _conventions.Layers = layers;
                Kept(() => PointerPathSettings.RememberDrawSettings(_conventions.Draw, changed));
            }
        }

        /// <summary>Keeps the choices made here for next time.</summary>
        private void Remember()
        {
            if (!_loading)
            {
                Kept(() => PointerPathSettings.RememberDrive(_conventions));
            }
        }

        private void Kept(Action save)
        {
            try
            {
                save();
                ShowUnitsDoubt();
            }
            catch (Exception ex)
            {
                // The choice still stands for this run.
                Say("These choices couldn't be kept for next time: " + ex.Message, warning: true);
            }
        }

        /// <summary>Warns, in red, when the drawing's size makes the units unlikely.</summary>
        private void ShowUnitsDoubt() => Say(SelectedUnits.Doubt(_drawingExtent), warning: true);

        private void Say(string? text, bool warning)
        {
            _note.Text = text ?? "";
            _note.ForeColor = warning ? DialogKit.Warning : SystemColors.GrayText;
            _note.Visible = !string.IsNullOrEmpty(text);
        }

        /// <summary>Turning the wheels on the spot is not how a vehicle is normally driven: red while it is on.</summary>
        private void ShowStationary()
        {
            _stationary.BackColor = _stationary.Checked ? DialogKit.WarningFill : Color.Transparent;
            _stationary.ForeColor = _stationary.Checked ? DialogKit.Warning : SystemColors.ControlText;
        }

        /// <summary>A vehicle's button: a radio button that looks like a push button and stays down.</summary>
        private RadioButton VehicleButton(string caption, string vehicle) => new RadioButton
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
