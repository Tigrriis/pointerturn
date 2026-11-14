namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The CAD program this build of the add-in runs in. The source is shared: the AutoCAD
    /// project (src/PointerPath.AutoCAD) compiles these same files with AUTOCAD defined, against
    /// the AutoCAD API, which the BricsCAD API mirrors name for name. What differs between the
    /// two is here, or under #if AUTOCAD where it is used.
    /// </summary>
    internal static class Host
    {
#if AUTOCAD
        public const string Name = "AutoCAD";

        /// <summary>The surfaces the ground clearance check can read levels from.</summary>
        public const string Surfaces = "Civil 3D TIN";

        public const string NoSurface =
            "No Civil 3D TIN surface was found in this drawing, so only a plotted long section can be checked. " +
            "Surfaces need Civil 3D: plain AutoCAD has none.";

        public const string SurfacesListed = "Only Civil 3D TIN surfaces are listed.";

        public const string NoSurfaceToSample = "There is no Civil 3D TIN surface in this drawing.";
#else
        public const string Name = "BricsCAD";

        /// <summary>The surfaces the ground clearance check can read levels from.</summary>
        public const string Surfaces = "BricsCAD TIN";

        public const string NoSurface =
            "No BricsCAD TIN surface was found in this drawing, so only a plotted long section can be checked. Civil Site Design keeps " +
            "its own surfaces: make a BricsCAD copy with CSD's Surface Exchange (Surfaces tab > BricsCAD Surfaces) and run this again.";

        public const string SurfacesListed = "Only BricsCAD TIN surfaces are listed. For a Civil Site Design surface, make a BricsCAD copy with CSD's Surface Exchange first.";

        public const string NoSurfaceToSample = "There is no BricsCAD TIN surface in this drawing. For a Civil Site Design surface, make a BricsCAD copy of it with CSD's Surface Exchange first.";
#endif
    }
}
