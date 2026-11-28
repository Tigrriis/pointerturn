using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The Turning template dialog (PTTEMPLATE): pick the vehicle, the turn angle or angles, left
    /// or right, and the straight travel before and after, GO. The turn is at the vehicle's
    /// minimum radius. Every choice is kept, so the dialog opens as it was left. See
    /// <see cref="VehicleDialog"/> for what it shares with the other dialogs.
    /// </summary>
    internal sealed class TemplateDialog : VehicleDialog
    {
        private readonly TextBox _angles;
        private readonly RadioButton _left;
        private readonly RadioButton _right;
        private readonly TextBox _leadIn;
        private readonly TextBox _leadOut;

        public TemplateDialog(TurnPathConventions conventions, string? insunits, UnitsWarning unitsWarning)
            : base("PointerPath - Turning template", "Turning template", conventions, insunits, unitsWarning)
        {
            TableLayoutPanel options = DialogKit.Table(2);
            options.Margin = new Padding(0, 2, 0, 0);

            _angles = DialogKit.Box(120);
            _angles.Text = string.Join(", ", conventions.TemplateAngles.Select(Num));
            Tips.SetToolTip(_angles, "One swept path for each angle: 90, or 30, 60, 90, 120, 150, 180 for a fan.");
            DialogKit.AddRow(options, Captioned("Turn angle(s), degrees"), _angles);

            _left = new RadioButton { Text = "Left", AutoSize = true, Checked = conventions.TemplateLeft, Margin = new Padding(3, 2, 8, 2) };
            _right = new RadioButton { Text = "Right", AutoSize = true, Checked = !conventions.TemplateLeft, Margin = new Padding(3, 2, 3, 2) };
            DialogKit.AddRow(options, Captioned("Direction"), DialogKit.Flow(_left, _right));

            _leadIn = DialogKit.Box(55);
            _leadIn.Text = Num(conventions.LeadIn);
            _leadOut = DialogKit.Box(55);
            _leadOut.Text = Num(conventions.LeadOut);
            Label slash = DialogKit.Caption("/");
            slash.Margin = new Padding(2, 5, 2, 0);
            DialogKit.AddRow(options, Captioned("Straight before / after (m)"), DialogKit.Flow(_leadIn, slash, _leadOut));

            // The direction is kept as it is clicked; what is typed, once it can be used, on leaving the box.
            _left.CheckedChanged += (s, e) =>
            {
                Conventions.TemplateLeft = _left.Checked;
                Remember();
            };
            foreach (TextBox box in new[] { _angles, _leadIn, _leadOut })
            {
                box.Leave += (s, e) =>
                {
                    if (Problem() == null)
                    {
                        Remember();
                    }
                };
            }

            Build("Turning template", "Choose the vehicle and the turn, then pick where it starts and which way it faces.", options);
        }

        /// <summary>Reads what is typed into the conventions; says what is wrong with it, or null.</summary>
        protected override string? Problem()
        {
            var angles = new List<double>();
            foreach (string part in _angles.Text.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryNum(part, out double angle) || angle <= 0 || angle > 360)
                {
                    return "Turn angles must be more than 0 and no more than 360 degrees, separated by commas.";
                }

                angles.Add(angle);
            }

            if (angles.Count == 0)
            {
                return "Enter at least one turn angle.";
            }

            if (!TryNum(_leadIn.Text, out double leadIn) || leadIn < 0 || !TryNum(_leadOut.Text, out double leadOut) || leadOut < 0)
            {
                return "The straight lengths before and after the turn must be 0 or more.";
            }

            Conventions.TemplateAngles = angles;
            Conventions.TemplateLeft = _left.Checked;
            Conventions.LeadIn = leadIn;
            Conventions.LeadOut = leadOut;
            return null;
        }

        protected override void Save() => PointerPathSettings.RememberTemplate(Conventions);

        /// <summary>A row's caption, flush with the dialog's left edge.</summary>
        private static Label Captioned(string text)
        {
            Label label = DialogKit.Caption(text);
            label.Margin = new Padding(0, 0, 14, 0);
            return label;
        }

        private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool TryNum(string text, out double value) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
