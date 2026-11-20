#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using CadApp = Bricscad.ApplicationServices.Application;
#endif

[assembly: CommandClass(typeof(PointerPath.BricsCAD.Commands))]

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// The PointerPath commands. On the ribbon, with the simplified dialogs:
    /// PTDRIVE     drive a design vehicle click by click: the Drive dialog (vehicle, options), then the drawing
    /// PTQDRIVE    the same without the dialog, with the vehicle and settings it was last left with
    /// PTTEMPLATE  draw a turning template: the Turning template dialog (vehicle, angle, direction), then its start
    /// PTLICENCE   sign in with a PointerPath account, see the licence on this computer, or sign out
    /// PTRIBBON    add the PointerPath ribbon tab again (if a workspace change removed it)
    ///
    /// Still here, typed, with the earlier dialog, until their own simplified dialogs are done:
    /// PTPATH      draw a design vehicle's swept path (turning template, along a path, or driven)
    /// PTSOLVE     work out a multi-point turn: the movements from a start to a target between the solve boundary layers
    /// PTINOUT     work out the way into a site, to a stop on it, and out again, forwards both ways
    /// PTEDIT      change a turning path (settings or geometry) and regenerate it
    /// PTCLEARANCE check a design vehicle's ground clearance (AS/NZS 2890.1 Appendix C) on a long section or a surface
    ///
    /// The commands that draw need a licence (see Licence): on a computer that isn't signed in
    /// they show the sign-in dialog first.
    /// </summary>
    public sealed class Commands
    {
        private const string Group = "POINTERPATH";

        [CommandMethod(Group, "PTDRIVE", CommandFlags.Modal)]
        public void PtDrive() => Licensed(doc => TurnPathCommand.Drive(doc));

        [CommandMethod(Group, "PTQDRIVE", CommandFlags.Modal)]
        public void PtQuickDrive() => Licensed(doc => TurnPathCommand.QuickDrive(doc));

        [CommandMethod(Group, "PTTEMPLATE", CommandFlags.Modal)]
        public void PtTemplate() => Licensed(doc => TurnPathCommand.Template(doc));

        [CommandMethod(Group, "PTPATH", CommandFlags.Modal)]
        public void PtPath() => Licensed(doc => TurnPathCommand.Run(doc));

        [CommandMethod(Group, "PTEDIT", CommandFlags.Modal)]
        public void PtEdit() => Licensed(doc => TurnPathCommand.Edit(doc));

        [CommandMethod(Group, "PTSOLVE", CommandFlags.Modal)]
        public void PtSolve() => Licensed(doc => TurnPathCommand.Run(doc, TurnPathChoice.ManoeuvreMode.Solve));

        [CommandMethod(Group, "PTINOUT", CommandFlags.Modal)]
        public void PtInOut() => Licensed(doc => TurnPathCommand.Run(doc, TurnPathChoice.ManoeuvreMode.SolveInOut));

        [CommandMethod(Group, "PTCLEARANCE", CommandFlags.Modal)]
        public void PtClearance() => Licensed(doc => GroundClearanceCommand.Run(doc));

        [CommandMethod(Group, "PTLICENCE", CommandFlags.Modal)]
        public void PtLicence() => Licence.ShowDialog(CadApp.DocumentManager.MdiActiveDocument?.Editor);

        /// <summary>The same, for those who spell it with an S.</summary>
        [CommandMethod(Group, "PTLICENSE", CommandFlags.Modal)]
        public void PtLicense() => PtLicence();

        [CommandMethod(Group, "PTRIBBON", CommandFlags.Modal)]
        public void PtRibbon()
        {
            string result = RibbonSetup.AddTab(activate: true);
            CadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nPointerPath: " + result + "\n");
        }

        /// <summary>Runs a command in the active drawing, once this computer is licensed.</summary>
        private static void Licensed(System.Action<Document> command)
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc != null && Licence.Ensure(doc.Editor))
            {
                command(doc);
            }
        }
    }
}
