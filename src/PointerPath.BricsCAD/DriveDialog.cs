using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The Drive dialog (PTDRIVE): pick the vehicle, tick what applies (Conflict settings has the
    /// layers a drive is checked against), GO. Every choice is kept
    /// as it is made, so the dialog opens as it was left and Quick drive (PTQDRIVE) can go
    /// without it. See <see cref="VehicleDialog"/> for what it shares with the other dialogs.
    /// </summary>
    internal sealed class DriveDialog : VehicleDialog
    {
        private readonly CheckBox _detect;
        private readonly CheckBox _stationary;
        private readonly Button _conflictSettings;

        public DriveDialog(TurnPathConventions conventions, string? insunits, UnitsWarning unitsWarning)
            : base("PointerPath - Drive", "Drive", conventions, insunits, unitsWarning)
        {
            _detect = DialogKit.Check("Detect conflicts");
            _detect.Margin = new Padding(3, 5, 3, 4);
            _detect.Checked = conventions.CheckConflicts;
            ShowConflictLayers();

            _conflictSettings = DialogKit.Button("Conflict settings...");
            _conflictSettings.Margin = new Padding(12, 0, 0, 2);
            Tips.SetToolTip(_conflictSettings, "The layers the swept path is checked against.");
            _conflictSettings.Click += (s, e) => EditConflictSettings();
            FlowLayoutPanel conflicts = DialogKit.Flow(_detect, _conflictSettings);
            _detect.Anchor = AnchorStyles.Left;

            _stationary = DialogKit.Check("Allow turn wheels while stopped");
            _stationary.Margin = new Padding(3, 2, 3, 4);
            _stationary.Padding = new Padding(3, 1, 6, 1);
            _stationary.Checked = conventions.DriveTurnWheelsWhenStationary;
            Tips.SetToolTip(_stationary, "The wheels are set before each forward move, so every leg is on its turning circle from the first metre: tighter, but it is steering on the spot.");
            ShowStationary();

            _detect.CheckedChanged += (s, e) =>
            {
                Conventions.CheckConflicts = _detect.Checked;
                Remember();
            };
            _stationary.CheckedChanged += (s, e) =>
            {
                ShowStationary();
                Conventions.DriveTurnWheelsWhenStationary = _stationary.Checked;
                Remember();
            };

            Build(conflicts, _stationary);
        }

        /// <summary>The layers a drive is checked against: no objects are picked in the drawing.</summary>
        private void EditConflictSettings()
        {
            using (var dialog = new ConflictSettingsDialog(Conventions))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                List<string> layers = dialog.Layers;
                Conventions.ObstructionLayers = layers;
                ShowConflictLayers();
                Kept(() => PointerPathSettings.RememberConflictLayers(layers));
            }
        }

        private void ShowConflictLayers() =>
            Tips.SetToolTip(_detect, "Flags where the swept path or its clearance meets linework on the layers in Conflict settings: " + string.Join(", ", Conventions.ConflictLayers) + ".");

        protected override void Save() => PointerPathSettings.RememberDrive(Conventions);

        /// <summary>Turning the wheels on the spot is not how a vehicle is normally driven: red while it is on.</summary>
        private void ShowStationary()
        {
            _stationary.BackColor = _stationary.Checked ? DialogKit.WarningFill : Color.Transparent;
            _stationary.ForeColor = _stationary.Checked ? DialogKit.Warning : SystemColors.ControlText;
        }
    }
}
