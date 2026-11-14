namespace PointerTurn.Core.Licensing
{
    /// <summary>
    /// What ties this build of PointerTurn to the licence server: the website's address and the
    /// public half of the key the website signs licences with. Both are compiled in, so they
    /// can't be swapped by editing a file next to the add-in.
    ///
    /// While they are empty the add-in does not ask for a licence at all: that is how it is
    /// built for development. The installer build (installer/Build-Installer.ps1) refuses to
    /// package a build without them unless told to.
    /// </summary>
    public static class LicenceBuild
    {
        /// <summary>The website, e.g. "https://www.example.com" (no trailing slash).</summary>
        public const string Website = "";

        /// <summary>
        /// The "x" and "y" of the public key: printed by `npm run licence-key` in website/, and
        /// served by the website at /api/v1/licence-key.
        /// </summary>
        public const string PublicKeyX = "";

        public const string PublicKeyY = "";
    }
}
