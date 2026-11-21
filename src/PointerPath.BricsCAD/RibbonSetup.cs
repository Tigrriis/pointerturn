using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Ribbon;
using Autodesk.Windows;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
using Bricscad.ApplicationServices;
using Bricscad.Ribbon;
using Bricscad.Windows;
using CadApp = Bricscad.ApplicationServices.Application;
#endif

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// Adds a "PointerPath" tab to the BricsCAD ribbon, with two panels:
    ///   Vehicles: Drive | Quick drive | Template
    ///   Account:  Licence
    /// Solve turn, In and out, Edit path and Clearance are off the ribbon until they have their
    /// simplified dialogs (see <see cref="ShowLaterCommands"/>); typed, they work as before.
    /// The ribbon is optional: if it is turned off, or BricsCAD rebuilds it (e.g. a
    /// workspace switch), the commands still work and PTRIBBON adds the tab again.
    ///
    /// BricsCAD exposes its ribbon through ComponentManager.Ribbon. RibbonServices'
    /// RibbonPaletteSet (the AutoCAD route) can be null there even while the ribbon shows.
    /// AutoCAD answers to both, so the same code serves it.
    /// </summary>
    internal static class RibbonSetup
    {
        private const string TabId = "POINTERPATH_TAB";

        /// <summary>
        /// Whether the commands that still use the earlier dialog (PTSOLVE, PTINOUT, PTEDIT,
        /// PTCLEARANCE) have ribbon buttons. Off while the dialogs are simplified, one command at a
        /// time: as each gets its new dialog, its button moves up with Drive.
        /// </summary>
        private static readonly bool ShowLaterCommands = false;
        private static bool _waitingForRibbon;
        private static bool _waitingForIdle;
        private static bool _watchingWorkspace;

        /// <summary>Called at startup. Adds the tab now, or as soon as the ribbon exists.</summary>
        public static void Initialize()
        {
            try
            {
                if (!_watchingWorkspace)
                {
                    _watchingWorkspace = true;
                    CadApp.SystemVariableChanged += OnSystemVariableChanged;
                }

                if (GetRibbon() != null)
                {
                    AddTab();
                    return;
                }

                // Not built yet (e.g. loaded at startup): try again once BricsCAD is idle,
                // and when the ribbon palette is created.
                if (!_waitingForIdle)
                {
                    _waitingForIdle = true;
                    CadApp.Idle += OnFirstIdle;
                }

                if (!_waitingForRibbon)
                {
                    _waitingForRibbon = true;
                    RibbonServices.RibbonPaletteSetCreated += OnRibbonCreated;
                }
            }
            catch (Exception)
            {
                // The ribbon is a convenience; never let it stop the add-in loading.
            }
        }

        /// <summary>
        /// Add the tab if it isn't there. With <paramref name="activate"/>, also switch the
        /// ribbon to it (used by PTRIBBON). Returns a message for the command line.
        /// </summary>
        public static string AddTab(bool activate = false)
        {
            try
            {
                RibbonControl? ribbon = GetRibbon();
                if (ribbon == null)
                {
                    return Host.Name + " didn't give the add-in access to the ribbon. If the ribbon is off, turn it on with RIBBON and run PTRIBBON again.";
                }

                RibbonTab? existing = ribbon.FindTab(TabId);
                if (existing != null)
                {
                    existing.IsVisible = true;
                    if (activate)
                    {
                        ribbon.ActiveTab = existing;
                    }

                    return "the PointerPath ribbon tab is already there" + (activate ? " (switched to it)." : ".");
                }

                var vehicles = new RibbonPanelSource { Id = "POINTERPATH_VEHICLES", Name = "Vehicles", Title = "Vehicles" };
                vehicles.Items.Add(Button("Drive", "PTDRIVE", "Drive a design vehicle click by click (AS/NZS 2890.1 B85 and B99 cars, AS 2890.2 SRV, MRV and HRV trucks): choose the vehicle, then click where it goes next; a click behind it reverses", "drive", large: true));
                vehicles.Items.Add(Button("Quick drive", "PTQDRIVE", "Drive straight away, with the vehicle and settings Drive was last left with", "quickdrive", large: true));
                vehicles.Items.Add(Button("Template", "PTTEMPLATE", "Draw a design vehicle's turning template: choose the vehicle, the turn angle and left or right, then pick the start point and direction", "turnpath", large: true));
                if (ShowLaterCommands)
                {
                    vehicles.Items.Add(Button("Solve turn", "PTSOLVE", "Work out a multi-point turn (2 points): pick where a car or rigid truck starts and where it ends up, and the movements between the kerbs, walls and columns on the solve boundary layers are found and drawn", "turnsolve", large: true));
                    vehicles.Items.Add(Button("In and out", "PTINOUT", "Show forward entry and exit from a site (3 points): pick where a car or rigid truck arrives, where it stops on the site and where it leaves, and the movements are found so it drives in forwards and out forwards", "turninout", large: true));
                    vehicles.Items.Add(Button("Edit path", "PTEDIT", "Change a turning path: its vehicle, radius, clearances, layers or geometry, then regenerate it", "turnedit", large: true));
                    vehicles.Items.Add(Button("Clearance", "PTCLEARANCE", "Check a B85 or B99 car's ground clearance (AS/NZS 2890.1 Appendix C) along a plotted long section, or on a surface along a path", "clearance", large: true));
                }

                var tab = new RibbonTab { Id = TabId, Name = "PointerPath", Title = "PointerPath" };
                tab.Panels.Add(new RibbonPanel { Source = vehicles });

                var account = new RibbonPanelSource { Id = "POINTERPATH_ACCOUNT", Name = "Account", Title = "Account" };
                account.Items.Add(Button("Licence", "PTLICENCE", "Sign in to PointerPath with your account, see the licence on this computer, or sign out", "licence", large: true));
                tab.Panels.Add(new RibbonPanel { Source = account });
                ribbon.Tabs.Add(tab);
                if (activate)
                {
                    ribbon.ActiveTab = tab;
                }

                return "added the PointerPath ribbon tab" + (activate ? " and switched to it." : ".");
            }
            catch (Exception ex)
            {
                return "could not add the ribbon tab: " + ex.Message;
            }
        }

        private static RibbonControl? GetRibbon()
        {
            try
            {
                RibbonControl? ribbon = ComponentManager.Ribbon;
                if (ribbon != null)
                {
                    return ribbon;
                }
            }
            catch (Exception)
            {
                // fall through to the AutoCAD-style route
            }

            try
            {
                return RibbonServices.RibbonPaletteSet?.RibbonControl;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void OnFirstIdle(object? sender, EventArgs e)
        {
            CadApp.Idle -= OnFirstIdle;
            _waitingForIdle = false;
            if (GetRibbon() != null)
            {
                AddTab();
            }
        }

        private static void OnRibbonCreated(object? sender, EventArgs e)
        {
            RibbonServices.RibbonPaletteSetCreated -= OnRibbonCreated;
            _waitingForRibbon = false;
            AddTab();
        }

        /// <summary>A workspace switch rebuilds the ribbon without our tab; put it back.</summary>
        private static void OnSystemVariableChanged(object? sender, SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
            {
                AddTab();
            }
        }

        private static RibbonButton Button(string text, string command, string toolTip, string icon, bool large)
        {
            var button = new RibbonButton
            {
                Id = "POINTERPATH_" + command,
                Name = text,
                Text = text,
                ToolTip = toolTip,
                ShowText = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
#if AUTOCAD
                // AutoCAD has no button style: a large button with its text underneath is a vertical one.
                Orientation = large ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal,
#else
                ButtonStyle = large ? RibbonButtonStyle.LargeWithText : RibbonButtonStyle.SmallWithText,
#endif
                CommandHandler = CommandHandler.Instance,
                CommandParameter = command,
            };

            ImageSource? small = LoadIcon(icon, 16);
            ImageSource? big = LoadIcon(icon, 32);
            button.ShowImage = small != null || big != null;
            if (small != null)
            {
                button.Image = small;
            }

            if (big != null)
            {
                button.LargeImage = big;
            }

            return button;
        }

        /// <summary>Icons are embedded PNGs (see tools/icons/make_icons.py).</summary>
        private static ImageSource? LoadIcon(string name, int size)
        {
            try
            {
                Stream? stream = typeof(RibbonSetup).Assembly.GetManifestResourceStream($"PointerPath.BricsCAD.Resources.{name}_{size}.png");
                if (stream == null)
                {
                    return null;
                }

                using (stream)
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception)
            {
                return null; // text-only button
            }
        }

        /// <summary>Runs a ribbon button's command.</summary>
        private sealed class CommandHandler : ICommand
        {
            public static readonly CommandHandler Instance = new CommandHandler();

            public event EventHandler? CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                try
                {
                    // The ribbon passes the clicked button, not its CommandParameter.
                    string? command = parameter as string ?? (parameter as RibbonCommandItem)?.CommandParameter as string;
                    switch (command)
                    {
                        case null:
                            Say($"the ribbon button gave no command ({parameter?.GetType().FullName ?? "nothing"} was passed). Type the command instead.");
                            return;
                        default:
                            Document? doc = CadApp.DocumentManager.MdiActiveDocument;
                            if (doc == null)
                            {
                                return; // no drawing open
                            }

                            doc.SendStringToExecute(command + " ", true, false, true);
                            return;
                    }
                }
                catch (Exception ex)
                {
                    // A failed click shouldn't take anything else down, but say why it did nothing.
                    Say("the ribbon button failed: " + ex.Message);
                }
            }

            private static void Say(string message)
            {
                try
                {
                    CadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nPointerPath: " + message + "\n");
                }
                catch (Exception)
                {
                    // Nowhere to report it.
                }
            }
        }
    }
}
