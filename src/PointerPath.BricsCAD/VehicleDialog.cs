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
    /// template): a heading, a card for each vehicle with its picture, the dialog's own options,
    /// and Draw settings, Units and GO in the strip along the bottom. Every choice is kept as it is
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
        /// The vehicles offered, by their name in turn-paths.json; each card shows the name and the
        /// vehicle's length. Later, once trailers are switched back on: the 19 m articulated
        /// vehicle, the A-double and the B-double, as a second row.
        /// </summary>
        private static readonly string[] Offered = { "B85", "B99", "SRV", "MRV", "HRV" };

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

            // Only shows when there is something to say: doubtful units, or a save that failed.
            _note = DialogKit.CautionNote(ButtonsPerRow * 108 - 8);
        }

        protected TurnPathConventions Conventions { get; }

        protected ToolTip Tips { get; } = new ToolTip { AutoPopDelay = 20000 };

        /// <summary>True while the dialog is being filled in from the conventions: nothing is kept yet.</summary>
        protected bool Loading => _loading;

        /// <summary>
        /// Lays the dialog out: the heading, the vehicles, the dialog's own <paramref name="options"/>
        /// (already showing what the conventions have), and Draw settings, Units and GO along the
        /// bottom. Called at the end of the constructor.
        /// </summary>
        /// <param name="heading">The dialog's name, as its heading ("Drive").</param>
        /// <param name="hint">A line under the heading saying what happens next.</param>
        protected void Build(string heading, string hint, params Control[] options)
        {
            // ---------------------------------------------------------------- vehicle
            TableLayoutPanel vehicles = DialogKit.Table(ButtonsPerRow);
            foreach (string name in Offered)
            {
                // The conventions decide what there is: a vehicle they don't have gets no card.
                string? key = Conventions.Vehicles.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (key == null)
                {
                    continue;
                }

                DesignVehicle vehicle = Conventions.Vehicles[key];
                var card = new VehicleCard(key, $"{vehicle.Length:0.##} m", VehicleIcon(key)) { Tag = key };
                Tips.SetToolTip(card, Describe(vehicle));
                vehicles.Controls.Add(card, _vehicles.Count % ButtonsPerRow, _vehicles.Count / ButtonsPerRow);
                _vehicles.Add(card);
            }

            vehicles.RowCount = (_vehicles.Count + ButtonsPerRow - 1) / ButtonsPerRow;
            vehicles.Margin = new Padding(0, 0, 0, 6);

            TableLayoutPanel content = DialogKit.Table(1);
            DialogKit.AddRow(content, DialogKit.Section("Vehicle"));
            DialogKit.AddRow(content, vehicles);
            foreach (Control option in options)
            {
                DialogKit.AddRow(content, option);
            }

            DialogKit.AddRow(content, _note);

            // ----------------------------------------------------------------- bottom
            Button drawSettings = DialogKit.Button("Draw settings...");
            drawSettings.Margin = new Padding(0, 0, 14, 0);
            drawSettings.Click += (s, e) => EditDrawSettings();

            Label unitsLabel = DialogKit.Caption("Units");
            unitsLabel.Margin = new Padding(0, 7, 6, 0);
            _units.Margin = new Padding(0, 3, 0, 0);

            var go = new PrimaryButton("GO") { DialogResult = DialogResult.OK };
            go.Font = new Font(Font, FontStyle.Bold);
            AcceptButton = go;

            DialogKit.Compose(this, heading, hint, content, DialogKit.Flow(drawSettings, unitsLabel, _units), go);

            ShowChoices();

            foreach (RadioButton button in _vehicles)
            {
                button.CheckedChanged += (s, e) =>
                {
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

        /// <summary>Warns, in a note under the options, when the drawing's size makes the units unlikely.</summary>
        private void ShowUnitsDoubt() => Say(_unitsWarning.Doubt(SelectedUnits));

        private void Say(string? warning)
        {
            _note.Text = warning ?? "";
            _note.Visible = !string.IsNullOrEmpty(warning);
        }

        /// <summary>
        /// The vehicle's side view (tools/icons/make_vehicle_icons.py draws them), at the size it
        /// was drawn: several times the size it is shown at. Null for a vehicle without one: its
        /// card is just its name and length.
        /// </summary>
        private static Image? VehicleIcon(string vehicle)
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
                    return new Bitmap(full); // a copy that doesn't need the stream kept open
                }
            }
            catch (Exception)
            {
                return null; // the name alone
            }
        }

        private static string Describe(DesignVehicle v) =>
            $"{v.Length:0.##} x {v.Width:0.##} m, wheelbase {v.Wheelbase:0.##} m, turns at its minimum radius of {v.MinTurnRadius:0.#} m. {v.Description}".TrimEnd();
    }
}
