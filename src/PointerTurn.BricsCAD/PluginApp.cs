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

[assembly: ExtensionApplication(typeof(PointerTurn.BricsCAD.PluginApp))]

namespace PointerTurn.BricsCAD
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

            CadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nPointerTurn loaded. Commands: PTPATH, PTSOLVE, PTINOUT, PTEDIT, PTCLEARANCE (or use the PointerTurn ribbon tab).\n");
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
