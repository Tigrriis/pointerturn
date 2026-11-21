using System.Drawing;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The Drive dialog (PTDRIVE): pick the vehicle, tick what applies, GO. Every choice is kept
    /// as it is made, so the dialog opens as it was left and Quick drive (PTQDRIVE) can go
    /// without it. See <see cref="VehicleDialog"/> for what it shares with the other dialogs.
    /// </summary>
    internal sealed class DriveDialog : VehicleDialog
    {
        private readonly CheckBox _detect;
        private readonly CheckBox _stationary;

        public DriveDialog(TurnPathConventions conventions, string? insunits, UnitsWarning unitsWarning)
            : base("PointerPath - Drive", "Drive", conventions, insunits, unitsWarning)
        {
            _detect = DialogKit.Check("Detect conflicts");
            _detect.Margin = new Padding(3, 2, 3, 4);
            _detect.Checked = conventions.CheckConflicts;
            Tips.SetToolTip(_detect, conventions.ObstructionLayers.Count > 0
                ? "Flags where the swept path or its clearance meets kerbs, walls and columns on: " + string.Join(", ", conventions.ObstructionLayers) + "."
                : "Flags where the swept path or its clearance meets kerbs, walls and columns. Once the vehicle is driven, you pick objects on the layers to check against.");

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

            Build(_detect, _stationary);
        }

        protected override void Save() => PointerPathSettings.RememberDrive(Conventions);

        /// <summary>Turning the wheels on the spot is not how a vehicle is normally driven: red while it is on.</summary>
        private void ShowStationary()
        {
            _stationary.BackColor = _stationary.Checked ? DialogKit.WarningFill : Color.Transparent;
            _stationary.ForeColor = _stationary.Checked ? DialogKit.Warning : SystemColors.ControlText;
        }
    }
}
