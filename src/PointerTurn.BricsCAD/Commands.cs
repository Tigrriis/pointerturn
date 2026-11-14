#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using CadApp = Bricscad.ApplicationServices.Application;
#endif

[assembly: CommandClass(typeof(PointerTurn.BricsCAD.Commands))]

namespace PointerTurn.BricsCAD
{
    /// <summary>
    /// The PointerTurn commands:
    /// PTPATH      draw a design vehicle's swept path (turning template, along a path, or driven)
    /// PTSOLVE     work out a multi-point turn: the movements from a start to a target between the solve boundary layers
    /// PTINOUT     work out the way into a site, to a stop on it, and out again, forwards both ways
    /// PTEDIT      change a turning path (settings or geometry) and regenerate it
    /// PTCLEARANCE check a design vehicle's ground clearance (AS/NZS 2890.1 Appendix C) on a long section or a surface
    /// PTRIBBON    add the PointerTurn ribbon tab again (if a workspace change removed it)
    /// </summary>
    public sealed class Commands
    {
        private const string Group = "POINTERTURN";

        [CommandMethod(Group, "PTPATH", CommandFlags.Modal)]
        public void PtPath() => TurnPathCommand.Run(CadApp.DocumentManager.MdiActiveDocument);

        [CommandMethod(Group, "PTEDIT", CommandFlags.Modal)]
        public void PtEdit() => TurnPathCommand.Edit(CadApp.DocumentManager.MdiActiveDocument);

        [CommandMethod(Group, "PTSOLVE", CommandFlags.Modal)]
        public void PtSolve() => TurnPathCommand.Run(CadApp.DocumentManager.MdiActiveDocument, TurnPathChoice.ManoeuvreMode.Solve);

        [CommandMethod(Group, "PTINOUT", CommandFlags.Modal)]
        public void PtInOut() => TurnPathCommand.Run(CadApp.DocumentManager.MdiActiveDocument, TurnPathChoice.ManoeuvreMode.SolveInOut);

        [CommandMethod(Group, "PTCLEARANCE", CommandFlags.Modal)]
        public void PtClearance() => GroundClearanceCommand.Run(CadApp.DocumentManager.MdiActiveDocument);

        [CommandMethod(Group, "PTRIBBON", CommandFlags.Modal)]
        public void PtRibbon()
        {
            string result = RibbonSetup.AddTab(activate: true);
            CadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nPointerTurn: " + result + "\n");
        }
    }
}
