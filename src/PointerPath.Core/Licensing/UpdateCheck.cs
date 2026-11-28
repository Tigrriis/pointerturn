using System;

namespace PointerPath.Core.Licensing
{
    /// <summary>
    /// Whether a newer PointerPath than the one running has been published. The website
    /// says which version is current each time it issues a licence; nothing extra is sent.
    /// </summary>
    public static class UpdateCheck
    {
        /// <summary>
        /// <paramref name="latest"/> if it is a later version than <paramref name="current"/>,
        /// else null: also when either can't be read as a version, so a typo on the website
        /// never tells everyone to update.
        /// </summary>
        public static string? NewerVersion(string? current, string? latest)
        {
            Version? running = Parse(current);
            Version? published = Parse(latest);
            return running != null && published != null && published > running ? latest!.Trim() : null;
        }

        /// <summary>The first three numbers of a version such as "1.2.0" or "v1.2.0.0".</summary>
        private static Version? Parse(string? text)
        {
            string value = (text ?? "").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(value.Contains(".") ? value : value + ".0", out Version? parsed) || parsed == null)
            {
                return null;
            }

            return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        }
    }
}
