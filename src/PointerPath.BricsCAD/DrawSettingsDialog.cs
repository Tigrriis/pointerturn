using System.Collections.Generic;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// Draw settings: which parts of a turning path are drawn, and the layer each goes on.
    /// Opened from the Drive dialog. OK hands back <see cref="Draw"/> and <see cref="Layers"/>;
    /// the caller keeps them as the ones to use from now on.
    ///
    /// What the earlier dialog also had (the swept path as one closed outline, vehicle outlines
    /// along the path, the label text height) isn't offered here: those keep the values in
    /// turn-paths.json.
    /// </summary>
    internal sealed class DrawSettingsDialog : Form
    {
        private static readonly (string Part, string Caption)[] LayerRows =
        {
            ("swept_path", "Swept path"), ("clearance", "Clearances"), ("vehicle", "Vehicle outline"),
            ("vehicle_intermediate", "Outlines along the path"), ("path", "Driven path"), ("wheel_paths", "Wheel paths"),
            ("labels", "Label"), ("hatch", "Hatch"), ("block", "Block"), ("conflict", "Conflicts"),
        };

        private readonly TurnPathDrawOptions _draw;
        private readonly TurnPathLayers _layers;
        private readonly CheckBox _swept;
        private readonly CheckBox _clearance;
        private readonly CheckBox _vehicle;
        private readonly CheckBox _path;
        private readonly CheckBox _wheels;
        private readonly CheckBox _labels;
        private readonly CheckBox _hatch;
        private readonly CheckBox _block;
        private readonly Dictionary<string, TextBox> _layerBoxes = new Dictionary<string, TextBox>();

        public DrawSettingsDialog(TurnPathConventions conventions)
        {
            // Copies: nothing changes unless the dialog is OK'd. Options not shown here ride along.
            _draw = conventions.Draw.Clone();
            _layers = conventions.Layers.Clone();

            DialogKit.Style(this, "PointerPath - Draw settings");

            TableLayoutPanel draw = DialogKit.Table(1);
            _swept = Tick(draw, "Swept path (body)", _draw.SweptPath);
            _clearance = Tick(draw, "Swept path + clearances", _draw.Clearance);
            _vehicle = Tick(draw, "Vehicle at start and end", _draw.Vehicle);
            _path = Tick(draw, "Driven path", _draw.Path);
            _wheels = Tick(draw, "Wheel paths", _draw.WheelPaths);
            _labels = Tick(draw, "Label", _draw.Labels);
            _hatch = Tick(draw, "Hatch the swept area", _draw.Hatch);
            _block = Tick(draw, "Make the result a block", _draw.Block);
            new ToolTip().SetToolTip(_block, "Named '<vehicle> turning path', inserted at the vehicle's start point on the Block layer.");

            TableLayoutPanel layers = DialogKit.Table(2);
            var current = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in _layers.All())
            {
                current[kv.Key] = kv.Value;
            }

            foreach (var (part, caption) in LayerRows)
            {
                TextBox box = DialogKit.Box(280);
                box.Text = current.TryGetValue(part, out string? name) ? name : "";
                DialogKit.AddRow(layers, DialogKit.Caption(caption), box);
                _layerBoxes[part] = box;
            }

            DialogKit.AddRow(layers, DialogKit.Note("New layers get the styles from turn-paths.json; existing layers are not changed.", 420));

            var ok = DialogKit.Button("OK");
            ok.DialogResult = DialogResult.OK;
            var cancel = DialogKit.Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            FlowLayoutPanel buttons = DialogKit.Flow(ok, cancel);
            buttons.Anchor = AnchorStyles.Right;
            AcceptButton = ok;
            CancelButton = cancel;

            TableLayoutPanel root = DialogKit.Table(2);
            GroupBox drawGroup = DialogKit.Group("Draw", draw);
            drawGroup.Margin = new Padding(0, 0, 12, 10);
            drawGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            GroupBox layerGroup = DialogKit.Group("Layers ({vehicle} is replaced by the vehicle name)", layers);
            layerGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            root.Controls.Add(drawGroup, 0, 0);
            root.Controls.Add(layerGroup, 1, 0);
            root.Controls.Add(buttons, 0, 1);
            root.SetColumnSpan(buttons, 2);
            root.Dock = DockStyle.Fill;
            Controls.Add(root);

            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK && Problem() is string problem)
                {
                    MessageBox.Show(this, problem, "Draw settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
        }

        /// <summary>What to draw, as ticked; valid once the dialog returned OK.</summary>
        public TurnPathDrawOptions Draw
        {
            get
            {
                TurnPathDrawOptions d = _draw.Clone();
                d.SweptPath = _swept.Checked;
                d.Clearance = _clearance.Checked;
                d.Vehicle = _vehicle.Checked;
                d.Path = _path.Checked;
                d.WheelPaths = _wheels.Checked;
                d.Labels = _labels.Checked;
                d.Hatch = _hatch.Checked;
                d.Block = _block.Checked;
                return d;
            }
        }

        /// <summary>The layer of each part, as typed; valid once the dialog returned OK.</summary>
        public TurnPathLayers Layers
        {
            get
            {
                TurnPathLayers l = _layers.Clone();
                foreach (KeyValuePair<string, TextBox> kv in _layerBoxes)
                {
                    l.Set(kv.Key, kv.Value.Text.Trim());
                }

                return l;
            }
        }

        private static CheckBox Tick(TableLayoutPanel table, string text, bool on)
        {
            CheckBox box = DialogKit.Check(text);
            box.Checked = on;
            DialogKit.AddRow(table, box);
            return box;
        }

        /// <summary>What is wrong with the layer names, or null.</summary>
        private string? Problem()
        {
            foreach (TextBox box in _layerBoxes.Values)
            {
                string layer = box.Text.Trim();
                if (layer.Length == 0)
                {
                    return "Every part needs a layer name.";
                }

                if (layer.IndexOfAny(new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' }) >= 0)
                {
                    return $"Layer name '{layer}' has characters {Host.Name} doesn't allow.";
                }
            }

            return null;
        }
    }
}
