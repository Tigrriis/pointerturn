namespace PointerPath.Core.Licensing
{
    /// <summary>
    /// What ties a build of PointerPath to the licence server: the website's address and the
    /// public half of the key the website signs licences with. They are compiled in, so they
    /// can't be swapped by editing a file next to the add-in.
    ///
    /// They are only compiled into a licensed build: one made with -p:Licensed=true, which is
    /// what the installer build (installer/Build-Installer.ps1) does. An ordinary build from
    /// source leaves them empty and does not ask for a licence at all, so the add-in can be
    /// developed and tried without an account.
    /// </summary>
    public static class LicenceBuild
    {
#if LICENSED
        /// <summary>The website (no trailing slash).</summary>
        public const string Website = "https://pointerturn-website-dn6i.onrender.com";

        /// <summary>
        /// The "x" and "y" of the website's public key: printed by `npm run licence-key` in
        /// website/, and served by the website at /api/v1/licence-key.
        /// </summary>
        public const string PublicKeyX = "TyfwJ-CUpMMSihX7dnNzzM1wkkaORvKIHd70kRD2DIM";

        public const string PublicKeyY = "j8XPWOkAn46jFtoxn9rv5ZXa3p-oFlBfWm-EsT6eK-c";
#else
        public const string Website = "";

        public const string PublicKeyX = "";

        public const string PublicKeyY = "";
#endif
    }
}
