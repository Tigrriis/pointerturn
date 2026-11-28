using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// Conflict settings: the layers whose linework (kerbs, walls, columns...) a drive is checked
    /// against when Detect conflicts is ticked. Opened from the Drive dialog. It starts from the
    /// default list; OK hands back <see cref="Layers"/> and the caller keeps them as the ones to
    /// use from now on. With the list set here, nothing is picked in the drawing.
    /// </summary>
    internal sealed class ConflictSettingsDialog : Form
    {
        private readonly TextBox _layers;

        public ConflictSettingsDialog(TurnPathConventions conventions)
        {
            DialogKit.Style(this, "PointerPath - Conflict settings");

            _layers = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Width = 436,
                Height = 170,
                Margin = new Padding(0, 0, 0, 0),
            };
            Show(conventions.ConflictLayers);

            Button defaults = DialogKit.Button("Defaults");
            defaults.Margin = new Padding(0);
            defaults.Click += (s, e) => Show(TurnPathConventions.DefaultConflictLayers);
            var ok = new PrimaryButton("OK") { DialogResult = DialogResult.OK };
            var cancel = DialogKit.Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel; // Enter makes a new line in the list, so OK is clicked

            TableLayoutPanel content = DialogKit.Table(1);
            DialogKit.AddRow(content, DialogKit.Section("Layers, one to a line"));
            DialogKit.AddRow(content, _layers);
            Label note = DialogKit.Note(
                "* and ? are wildcards, and capitals don't matter: *KERB* finds every layer with KERB in its name, in xrefs too. " +
                "Frozen and off layers are ignored. Defaults puts the standard list back.", 436);
            note.Margin = new Padding(0, 6, 0, 0);
            DialogKit.AddRow(content, note);

            DialogKit.Compose(this, "Conflict settings", "The layers the swept path is checked against when Detect conflicts is ticked.", content, defaults, ok, cancel);

            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK && Layers.Count == 0)
                {
                    MessageBox.Show(this, "Enter at least one layer, or press Defaults for the standard list.", "Conflict settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
        }

        /// <summary>The layers and patterns as typed; valid once the dialog returned OK.</summary>
        public List<string> Layers => LayerPattern.Parse(_layers.Text.Replace("\r", ""))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private void Show(IEnumerable<string> layers)
        {
            _layers.Text = string.Join(Environment.NewLine, layers);
            _layers.SelectionStart = 0;
            _layers.SelectionLength = 0;
        }
    }
}
