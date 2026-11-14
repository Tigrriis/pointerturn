using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointerTurn.Core.Licensing
{
    /// <summary>
    /// What a signed-in computer keeps between sessions, in one small file in the user's
    /// profile: the activation token (traded with the website for fresh licences; this is what
    /// "signed in" means) and the latest signed licence.
    /// </summary>
    public sealed class LicenceStore
    {
        [JsonPropertyName("activation_token")]
        public string? ActivationToken { get; set; }

        [JsonPropertyName("licence_token")]
        public string? LicenceToken { get; set; }

        /// <summary>When the licence was last fetched from the website.</summary>
        [JsonPropertyName("last_refresh")]
        public DateTime? LastRefreshUtc { get; set; }

        /// <summary>
        /// The latest time PointerTurn has seen. A clock set back to before this doesn't make an
        /// expired licence work again.
        /// </summary>
        [JsonPropertyName("last_seen")]
        public DateTime? LastSeenUtc { get; set; }

        /// <summary>Why the licence was taken away, to show the next time sign-in is asked for.</summary>
        [JsonPropertyName("note")]
        public string? Note { get; set; }

        /// <summary>The saved state, or an empty one when there is no file or it can't be read.</summary>
        public static LicenceStore Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<LicenceStore>(File.ReadAllText(path)) ?? new LicenceStore();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                // Unreadable: the same as not signed in.
            }

            return new LicenceStore();
        }

        public void Save(string path)
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder!);
            }

            // Written beside the file and moved over it, so a crash can't leave half a file.
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        public static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Nothing more to do: the caller has already cleared what it holds.
            }
        }
    }
}
