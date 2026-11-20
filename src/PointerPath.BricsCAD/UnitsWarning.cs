using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The question asked when the units look wrong for the size of the drawing (a "millimetre"
    /// drawing smaller than a car, say): carry on in them, or not? It is asked once for each
    /// drawing file. Once it has been answered, yes or no, that file isn't asked about again
    /// while the CAD program stays open: the dialogs still show the warning in red, and the
    /// commands say it on the command line.
    /// </summary>
    internal sealed class UnitsWarning
    {
        /// <summary>The drawing files the question has been answered for, by full name.</summary>
        private static readonly HashSet<string> Answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly string _file;

        /// <param name="file">The drawing's file name, with its folder (the document's name).</param>
        /// <param name="drawingExtent">How big the drawing is (the larger of its width and height, in drawing units), if known.</param>
        public UnitsWarning(string file, double? drawingExtent)
        {
            _file = file ?? "";
            Extent = drawingExtent;
        }

        public double? Extent { get; }

        /// <summary>A reason to doubt these units in this drawing, or null.</summary>
        public string? Doubt(DrawingUnits units) => units.Doubt(Extent);

        /// <summary>The question has been answered for this drawing file, so it isn't asked again.</summary>
        public bool AlreadyAnswered => Answered.Contains(_file);

        /// <summary>
        /// Whether to go ahead in these units. Asks only when they are in doubt and this drawing
        /// file hasn't been asked about before; otherwise it is yes.
        /// </summary>
        /// <param name="hint">Added to the question, e.g. where the units are changed.</param>
        public bool CarryOn(IWin32Window? owner, DrawingUnits units, string? hint = null)
        {
            string? doubt = Doubt(units);
            if (doubt == null || AlreadyAnswered)
            {
                return true;
            }

            // The wrong units would draw the vehicle a thousand times too big or too small.
            DialogResult answer = MessageBox.Show(owner, doubt + "\n\nCarry on in " + units.Name + "?" + (hint != null ? " " + hint : ""),
                "Drawing units", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            Answered.Add(_file);
            return answer == DialogResult.Yes;
        }
    }
}
