using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using CadApp = Bricscad.ApplicationServices.Application;
#endif

[assembly: ExtensionApplication(typeof(PointerPath.BricsCAD.PluginApp))]

namespace PointerPath.BricsCAD
{
    /// <summary>Entry point the CAD program calls when the DLL is loaded (NETLOAD or autoload).</summary>
    public sealed class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            // Must run before anything touches System.Text.Json or its dependencies.
            DependencyResolver.Install();
            Startup();
        }

        public void Terminate()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Startup()
        {
            RibbonSetup.Initialize();

            // Where the licence stands, read from the user's profile (no network), and a
            // refresh in the background if the one held is getting old.
            string licence = "";
            string update = "";
            try
            {
                PointerPath.Core.Licensing.LicenceStatus status = Licence.Manager.Check();
                if (status.State == PointerPath.Core.Licensing.LicenceState.SignedOut)
                {
                    licence = " Not signed in: run PTLICENCE to sign in with your PointerPath account.";
                }

                // A newer version, if the website mentioned one the last time it was reached.
                update = Licence.UpdateNotice();
                Licence.RefreshInBackground();
            }
            catch (System.Exception)
            {
                // The commands check again, and say what is wrong, when they are run.
            }

            CadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nPointerPath loaded. Commands: PTDRIVE, PTQDRIVE, PTTEMPLATE, PTLICENCE (or use the PointerPath ribbon tab). " +
                "Typed only, while their dialogs are reworked: PTPATH, PTSOLVE, PTINOUT, PTEDIT, PTCLEARANCE." + licence + "\n" +
                (update.Length > 0 ? update + "\n" : ""));
        }
    }

    /// <summary>
    /// On .NET Framework (BricsCAD V24/V25, AutoCAD 2021 to 2024) every add-in shares one AppDomain and the
    /// add-in's own binding redirects are ignored, so the libraries it ships with
    /// (System.Text.Json, System.Memory, ...) can fail to bind to the versions we ship.
    /// This resolves them from the add-in folder by simple name. On .NET 8 (BricsCAD V26,
    /// AutoCAD 2025) they are part of the runtime, so the handler is not installed.
    /// </summary>
    internal static class DependencyResolver
    {
#if NETFRAMEWORK
        private static bool _installed;
#endif

        public static void Install()
        {
#if NETFRAMEWORK
            if (_installed)
            {
                return;
            }

            _installed = true;
            string folder = Path.GetDirectoryName(typeof(DependencyResolver).Assembly.Location) ?? ".";
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string name = new AssemblyName(args.Name).Name ?? "";
                string candidate = Path.Combine(folder, name + ".dll");
                if (!File.Exists(candidate))
                {
                    return null;
                }

                // Reuse a copy that is already loaded (acts like a binding redirect).
                Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                return loaded ?? Assembly.LoadFrom(candidate);
            };
#endif
        }
    }
}
