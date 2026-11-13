namespace PointerTurn.Core.Drawing
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
    }
}
