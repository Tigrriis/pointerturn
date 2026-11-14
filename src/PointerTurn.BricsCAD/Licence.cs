using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using PointerTurn.Core.Licensing;
#if AUTOCAD
using Autodesk.AutoCAD.EditorInput;
#else
using Bricscad.EditorInput;
#endif

namespace PointerTurn.BricsCAD
{
    /// <summary>
    /// The licence check in front of the commands. A computer is signed in once with a
    /// PointerTurn account (PTLICENCE, or the dialog a command shows when it isn't); after that
    /// the commands run from the signed licence saved in the user's profile, which is refreshed
    /// in the background about once a day when the computer is online.
    ///
    /// A build with no licence server compiled in (LicenceBuild) doesn't ask for a licence.
    /// </summary>
    internal static class Licence
    {
        private static readonly object Gate = new object();
        private static LicenceManager? _manager;
        private static bool _refreshing;

        public static LicenceManager Manager
        {
            get
            {
                lock (Gate)
                {
                    return _manager ?? (_manager = Create());
                }
            }
        }

        /// <summary>What this computer is called on the account page.</summary>
        public static string MachineName => $"{Environment.MachineName} ({Environment.UserName})";

        /// <summary>
        /// True when the command may go ahead. When this computer isn't licensed, asks the
        /// website for a fresh licence if it was signed in before, and otherwise shows the
        /// sign-in dialog.
        /// </summary>
        public static bool Ensure(Editor? ed)
        {
            LicenceStatus status;
            try
            {
                status = Manager.Check();
                if (status.State == LicenceState.Licensed)
                {
                    RefreshInBackground();
                }
                else if (status.State == LicenceState.SignedOut && status.CanRefresh)
                {
                    // Signed in, but the licence held has run out or was given up: one quiet
                    // try at a new one before asking for the password again.
                    Cursor previous = Cursor.Current;
                    Cursor.Current = Cursors.WaitCursor;
                    try
                    {
                        status = Task.Run(() => Manager.RefreshAsync()).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        Cursor.Current = previous;
                    }
                }

                if (status.MayRun)
                {
                    return true;
                }

                using (var dialog = new LicenceDialog(Manager, status))
                {
                    dialog.ShowDialog();
                }

                if (Manager.Check().MayRun)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\nPointerTurn: the licence could not be checked (" + ex.Message + ").\n");
                return false;
            }

            ed?.WriteMessage("\nPointerTurn: sign in with your PointerTurn account to use this command (PTLICENCE).\n");
            return false;
        }

        /// <summary>PTLICENCE: sign in, see the licence on this computer, or sign out.</summary>
        public static void ShowDialog(Editor? ed)
        {
            try
            {
                using (var dialog = new LicenceDialog(Manager, Manager.Check()))
                {
                    dialog.ShowDialog();
                }

                ed?.WriteMessage("\nPointerTurn: " + Summary(Manager.Check()) + "\n");
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\nPointerTurn: the licence could not be checked (" + ex.Message + ").\n");
            }
        }

        /// <summary>Where this computer stands, in a line.</summary>
        public static string Summary(LicenceStatus status)
        {
            switch (status.State)
            {
                case LicenceState.Licensed:
                    return $"signed in as {status.Email}.";
                case LicenceState.NotConfigured:
                    return "this build does not check for a licence.";
                default:
                    return "not signed in. Run PTLICENCE to sign in with your PointerTurn account.";
            }
        }

        /// <summary>Fetches a newer licence if the one held is about a day old. Never blocks, never throws.</summary>
        public static void RefreshInBackground()
        {
            try
            {
                lock (Gate)
                {
                    if (_refreshing || !Manager.RefreshDue)
                    {
                        return;
                    }

                    _refreshing = true;
                }

                LicenceManager manager = Manager;
                Task.Run(async () =>
                {
                    try
                    {
                        await manager.RefreshAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Tried again at the next command.
                    }
                    finally
                    {
                        lock (Gate)
                        {
                            _refreshing = false;
                        }
                    }
                });
            }
            catch (Exception)
            {
                // A refresh is a courtesy; the licence held still stands.
            }
        }

        private static LicenceManager Create()
        {
#if NETFRAMEWORK
            // The CAD program decides the defaults for the whole process; make sure TLS 1.2 is among them.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
#endif
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PointerTurn");
            string version = typeof(Licence).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            return new LicenceManager(
                LicenceBuild.Website,
                new LicencePublicKey(LicenceBuild.PublicKeyX, LicenceBuild.PublicKeyY),
                Path.Combine(folder, "licence.json"),
                MachineId(),
                new LicenceServer(LicenceBuild.Website, $"PointerTurn/{version} ({Host.Name})"));
        }

        /// <summary>
        /// What identifies this computer and Windows user to the website: a hash, so nothing
        /// about the PC itself is sent. Each Windows user on a shared PC takes a place of their
        /// own, so two people signed in with one account don't sign each other out.
        /// </summary>
        private static string MachineId()
        {
            string machine = "";
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey? key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    machine = key?.GetValue("MachineGuid") as string ?? "";
                }
            }
            catch (Exception)
            {
                // Falls back to the computer's name below.
            }

            if (machine.Length == 0)
            {
                machine = Environment.MachineName;
            }

            string source = "PointerTurn|" + machine + "|" + Environment.UserDomainName + "\\" + Environment.UserName;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source.ToLowerInvariant()));
                var text = new StringBuilder(32);
                for (int i = 0; i < 16; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }

                return text.ToString();
            }
        }
    }
}
