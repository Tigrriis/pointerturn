using System;
using System.Threading.Tasks;

namespace PointerPath.Core.Licensing
{
    public enum LicenceState
    {
        /// <summary>A signed licence for this computer that has not run out: PointerPath may run.</summary>
        Licensed,

        /// <summary>No usable licence here: the user needs to sign in.</summary>
        SignedOut,

        /// <summary>This build has no licence server or key compiled in, so it does not ask for a licence.</summary>
        NotConfigured,
    }

    /// <summary>Where this computer stands, and what to tell the user about it.</summary>
    public sealed class LicenceStatus
    {
        public LicenceState State { get; set; }

        /// <summary>True when the commands may run.</summary>
        public bool MayRun => State != LicenceState.SignedOut;

        /// <summary>The account the licence belongs to, when licensed.</summary>
        public string Email { get; set; } = "";

        /// <summary>When the licence held here runs out unless it is refreshed first.</summary>
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>Whether the website can be asked for a fresh licence without a password.</summary>
        public bool CanRefresh { get; set; }

        /// <summary>Why there is no licence, in words for the user. Empty when there is nothing to explain.</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Whether this computer may run PointerPath, and signing in and out. Works from the saved
    /// licence without a connection; talks to the website only to sign in, to refresh the
    /// licence (about once a day, when online) and to sign out.
    /// </summary>
    public sealed class LicenceManager
    {
        /// <summary>How long after fetching a licence before asking the website for a newer one.</summary>
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(20);

        private readonly object _gate = new object();
        private readonly string _website;
        private readonly LicencePublicKey _key;
        private readonly string _storePath;
        private readonly string _machineId;
        private readonly ILicenceServer _server;
        private readonly Func<DateTime> _utcNow;

        /// <param name="website">The website's address (LicenceBuild.Website). Empty switches licensing off.</param>
        /// <param name="key">The key licences are signed with (LicenceBuild.PublicKeyX and Y).</param>
        /// <param name="storePath">The file this computer's sign-in is kept in.</param>
        /// <param name="machineId">What identifies this computer (and Windows user) to the website.</param>
        public LicenceManager(string website, LicencePublicKey key, string storePath, string machineId, ILicenceServer server, Func<DateTime>? utcNow = null)
        {
            _website = (website ?? "").TrimEnd('/');
            _key = key;
            _storePath = storePath;
            _machineId = machineId;
            _server = server;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Whether this build asks for a licence at all.</summary>
        public bool IsConfigured => _website.Length > 0 && _key.IsSet;

        public string Website => _website;

        /// <summary>
        /// The version the website was offering for download the last time it issued this
        /// computer a licence, or null if it hasn't said. Read from what is saved here.
        /// </summary>
        public string? LatestVersion
        {
            get
            {
                if (!IsConfigured)
                {
                    return null;
                }

                lock (_gate)
                {
                    return LicenceStore.Load(_storePath).LatestVersion;
                }
            }
        }

        /// <summary>Where this computer stands, from what is saved here. Never touches the network.</summary>
        public LicenceStatus Check()
        {
            if (!IsConfigured)
            {
                return new LicenceStatus { State = LicenceState.NotConfigured };
            }

            lock (_gate)
            {
                LicenceStore store = LicenceStore.Load(_storePath);
                DateTime now = Now(store);
                return Evaluate(store, now);
            }
        }

        /// <summary>
        /// True when it is time to ask the website for a newer licence: signed in, and the one
        /// held was fetched a while ago (or the clock says it was fetched in the future).
        /// </summary>
        public bool RefreshDue
        {
            get
            {
                if (!IsConfigured)
                {
                    return false;
                }

                lock (_gate)
                {
                    LicenceStore store = LicenceStore.Load(_storePath);
                    if (string.IsNullOrEmpty(store.ActivationToken))
                    {
                        return false;
                    }

                    DateTime now = _utcNow();
                    return store.LastRefreshUtc == null
                        || now - store.LastRefreshUtc.Value >= RefreshAfter
                        || store.LastRefreshUtc.Value > now.AddMinutes(5);
                }
            }
        }

        /// <summary>
        /// Asks the website for a fresh licence and returns where that leaves this computer.
        /// With no connection, what is held here stands. If the website says the computer was
        /// signed out, or the licence has ended, the licence held here is given up at once.
        /// </summary>
        public async Task<LicenceStatus> RefreshAsync()
        {
            if (!IsConfigured)
            {
                return new LicenceStatus { State = LicenceState.NotConfigured };
            }

            string? activation;
            lock (_gate)
            {
                activation = LicenceStore.Load(_storePath).ActivationToken;
            }

            if (string.IsNullOrEmpty(activation))
            {
                return Check();
            }

            LicenceReply reply = await _server.RefreshAsync(activation!).ConfigureAwait(false);

            lock (_gate)
            {
                LicenceStore store = LicenceStore.Load(_storePath);
                if (store.ActivationToken != activation)
                {
                    // Signed out, or in again, while the website was answering: that stands.
                    return Evaluate(store, Now(store));
                }

                if (reply.Ok && Usable(reply.LicenceToken) != null)
                {
                    store.LicenceToken = reply.LicenceToken;
                    store.LastRefreshUtc = _utcNow();
                    store.LatestVersion = string.IsNullOrWhiteSpace(reply.LatestVersion) ? store.LatestVersion : reply.LatestVersion;
                    store.Note = null;
                }
                else if (reply.Error == "invalid_activation")
                {
                    store.ActivationToken = null;
                    store.LicenceToken = null;
                    store.Note = "This computer was signed out of PointerPath from your account page. Sign in again to use it here.";
                }
                else if (reply.Error == "licence_inactive")
                {
                    // The sign-in is kept: if the licence is renewed, the next refresh brings it back.
                    store.LicenceToken = null;
                    store.Note = $"Your PointerPath licence has ended. Renew it at {_website}/account, then sign in here again.";
                }
                else
                {
                    // No connection, or an answer that makes no sense: keep what is held.
                    return Evaluate(store, Now(store));
                }

                TrySave(store);
                return Evaluate(store, Now(store));
            }
        }

        /// <summary>Signs this computer in with the account's email and password.</summary>
        /// <param name="machineName">What to call this computer on the account page.</param>
        public async Task<LicenceStatus> SignInAsync(string email, string password, string machineName)
        {
            if (!IsConfigured)
            {
                return new LicenceStatus { State = LicenceState.NotConfigured };
            }

            email = (email ?? "").Trim();
            if (email.Length == 0 || string.IsNullOrEmpty(password))
            {
                return Refused("Enter your account's email address and password.");
            }

            LicenceReply reply = await _server.ActivateAsync(email, password, _machineId, machineName ?? "").ConfigureAwait(false);
            if (!reply.Ok)
            {
                return Refused(Explain(reply));
            }

            if (string.IsNullOrEmpty(reply.ActivationToken) || Usable(reply.LicenceToken) == null)
            {
                return Refused("The licence the website sent could not be checked. This copy of PointerPath may be out of date: download the current installer from your account page.");
            }

            lock (_gate)
            {
                var store = new LicenceStore
                {
                    ActivationToken = reply.ActivationToken,
                    LicenceToken = reply.LicenceToken,
                    LastRefreshUtc = _utcNow(),
                    LastSeenUtc = _utcNow(),
                    LatestVersion = string.IsNullOrWhiteSpace(reply.LatestVersion) ? null : reply.LatestVersion,
                };
                if (!TrySave(store))
                {
                    return Refused($"You are signed in, but PointerPath could not save that to {_storePath}. Check that the folder can be written to.");
                }

                return Evaluate(store, _utcNow());
            }
        }

        /// <summary>Signs this computer out, freeing its place on the licence.</summary>
        public async Task SignOutAsync()
        {
            string? activation;
            lock (_gate)
            {
                activation = LicenceStore.Load(_storePath).ActivationToken;
                LicenceStore.Delete(_storePath);
            }

            if (!string.IsNullOrEmpty(activation) && IsConfigured)
            {
                // Best effort: with no connection the place stays taken until it is freed on
                // the account page.
                await _server.DeactivateAsync(activation!).ConfigureAwait(false);
            }
        }

        // ---- helpers ----

        private LicenceStatus Evaluate(LicenceStore store, DateTime now)
        {
            bool canRefresh = !string.IsNullOrEmpty(store.ActivationToken);
            if (string.IsNullOrEmpty(store.LicenceToken))
            {
                return new LicenceStatus { State = LicenceState.SignedOut, CanRefresh = canRefresh, Message = store.Note ?? "" };
            }

            SignedLicence? licence = SignedLicence.TryRead(store.LicenceToken, _key, out string problem);
            if (licence == null)
            {
                return new LicenceStatus { State = LicenceState.SignedOut, CanRefresh = canRefresh, Message = problem + " Sign in again." };
            }

            if (licence.MachineId != _machineId)
            {
                return new LicenceStatus
                {
                    State = LicenceState.SignedOut,
                    Message = "The licence saved here was issued to another computer or Windows user. Sign in again.",
                };
            }

            if (now >= licence.ExpiresUtc)
            {
                return new LicenceStatus
                {
                    State = LicenceState.SignedOut,
                    CanRefresh = canRefresh,
                    Email = licence.Email,
                    ExpiresUtc = licence.ExpiresUtc,
                    Message = $"PointerPath has not been able to check your licence since {store.LastRefreshUtc ?? licence.IssuedUtc:d MMMM yyyy}. Connect to the internet and try again.",
                };
            }

            return new LicenceStatus
            {
                State = LicenceState.Licensed,
                CanRefresh = canRefresh,
                Email = licence.Email,
                ExpiresUtc = licence.ExpiresUtc,
            };
        }

        /// <summary>The licence in <paramref name="token"/> if it is signed, for this computer and current.</summary>
        private SignedLicence? Usable(string? token)
        {
            SignedLicence? licence = SignedLicence.TryRead(token, _key, out _);
            return licence != null && licence.MachineId == _machineId && licence.ExpiresUtc > _utcNow() ? licence : null;
        }

        /// <summary>
        /// The time to judge expiry by: the clock, but never earlier than the latest time already
        /// seen. Moves that mark forward (saving it at most hourly).
        /// </summary>
        private DateTime Now(LicenceStore store)
        {
            DateTime clock = _utcNow();
            DateTime seen = store.LastSeenUtc ?? DateTime.MinValue;
            if (clock <= seen)
            {
                return seen;
            }

            if (clock - seen >= TimeSpan.FromHours(1) && (store.LicenceToken != null || store.ActivationToken != null))
            {
                store.LastSeenUtc = clock;
                TrySave(store);
            }

            return clock;
        }

        private bool TrySave(LicenceStore store)
        {
            try
            {
                store.Save(_storePath);
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static LicenceStatus Refused(string message)
        {
            return new LicenceStatus { State = LicenceState.SignedOut, Message = message };
        }

        private string Explain(LicenceReply reply)
        {
            switch (reply.Error)
            {
                case "invalid_credentials":
                    return "That email and password do not match an account.";
                case "email_not_verified":
                    return "Your email address has not been confirmed yet. Open the link we emailed you, then sign in here again.";
                case "no_licence":
                    return $"This account has no active licence. Buy one at {_website}, then sign in here again.";
                case "machine_limit":
                    string where = reply.Machines.Count > 0 ? " (" + string.Join(", ", reply.Machines) + ")" : "";
                    return $"Your licence is already in use on as many computers as it allows{where}. Sign one out at {_website}/account, then sign in here again.";
                case "region_not_allowed":
                    return "PointerPath is not available in the country this computer appears to be in. If that is wrong, check whether a VPN is in use, then try again.";
                case LicenceReply.Network:
                    return $"PointerPath could not reach {_website}. Check your internet connection and try again.";
                case LicenceReply.TooManyAttempts:
                    return "Too many attempts. Wait a few minutes and try again.";
                default:
                    return $"Signing in did not work ({reply.Error}). Try again in a moment.";
            }
        }
    }
}
