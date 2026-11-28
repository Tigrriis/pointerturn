namespace PointerPath.Core.Drawing
{
    /// <summary>How a layer is set up when it has to be created: only applied to layers that don't exist yet.</summary>
    public sealed class LayerStyle
    {
        public short? Color { get; set; }
        public int[]? Rgb { get; set; }
        public string? Linetype { get; set; }
        public double? Lineweight { get; set; }
        public bool? Plot { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>The hatch a swept area is filled with.</summary>
    public sealed class HatchStyle
    {
        public string Layer { get; set; } = "0";
        public string Pattern { get; set; } = "SOLID";
        public double Scale { get; set; } = 1;
        public double Angle { get; set; }

        /// <summary>
        /// How transparent the hatch is, percent: 0 is solid, 90 the most the CAD programs allow.
        /// What is under it (kerbs, an aerial photo) shows through.
        /// </summary>
        public int Transparency { get; set; } = 60;
    }
}
