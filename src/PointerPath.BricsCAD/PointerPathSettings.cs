using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointerPath.Core.Vehicles;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// Per-user settings in %APPDATA%\PointerPath\settings.json (optional: the file only needs to
    /// exist to point at a shared copy of the turning path conventions), and where the
    /// conventions themselves are found.
    /// </summary>
    internal sealed class PointerPathSettings
    {
        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PointerPath");

        public static string SettingsPath => Path.Combine(Folder, "settings.json");

        /// <summary>
        /// Turning path conventions file (vehicles, clearances, layers), e.g. a shared office copy on
        /// a network drive. Empty means %APPDATA%\PointerPath\turn-paths.json if a dialog has saved
        /// one, else turn-paths.json next to the add-in DLL.
        /// </summary>
        [JsonPropertyName("turn_paths_file")]
        public string? TurnPathsFile { get; set; }

        public static PointerPathSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    return JsonSerializer.Deserialize<PointerPathSettings>(File.ReadAllText(SettingsPath)) ?? new PointerPathSettings();
                }
            }
            catch (Exception)
            {
                // An unreadable settings file falls back to the defaults.
            }

            return new PointerPathSettings();
        }

        /// <summary>Where the dialogs save the user's turning path conventions.</summary>
        public static string UserTurnPathsPath => Path.Combine(Folder, "turn-paths.json");

        /// <summary>
        /// Where CSD-Bridge, which these tools were part of, saved them. Read when PointerPath has no
        /// saved copy of its own yet, so defaults saved there carry over; never written.
        /// </summary>
        private static string LegacyUserTurnPathsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CsdBridge", "turn-paths.json");

        private static string ShippedTurnPathsPath =>
            Path.Combine(Path.GetDirectoryName(typeof(PointerPathSettings).Assembly.Location) ?? ".", "turn-paths.json");

        public string ResolvedTurnPathsPath()
        {
            if (!string.IsNullOrWhiteSpace(TurnPathsFile))
            {
                return Environment.ExpandEnvironmentVariables(TurnPathsFile!);
            }

            if (File.Exists(UserTurnPathsPath))
            {
                return UserTurnPathsPath;
            }

            if (File.Exists(LegacyUserTurnPathsPath))
            {
                return LegacyUserTurnPathsPath;
            }

            return ShippedTurnPathsPath;
        }

        /// <summary>The turning path conventions, or null with the reason.</summary>
        public TurnPathConventions? LoadTurnPathConventions(out string? error)
        {
            string path = ResolvedTurnPathsPath();
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = $"{path} not found";
                    return null;
                }

                TurnPathConventions conventions = TurnPathConventions.Load(path);

                // A copy saved by "Save as defaults", or a shared one, may predate vehicles added to the
                // file shipped with the add-in: take those from the shipped file.
                string shipped = ShippedTurnPathsPath;
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(shipped), StringComparison.OrdinalIgnoreCase) && File.Exists(shipped))
                {
                    try
                    {
                        conventions.AddMissingVehicles(TurnPathConventions.Load(shipped));
                    }
                    catch (Exception)
                    {
                        // The user's own file is good: a problem with the shipped one is not theirs to see here.
                    }
                }

                return conventions;
            }
            catch (Exception ex)
            {
                error = $"{path}: {ex.Message}";
                return null;
            }
        }
    }
}
