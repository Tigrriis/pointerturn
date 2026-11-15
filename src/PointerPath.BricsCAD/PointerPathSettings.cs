using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public TurnPathConventions? LoadTurnPathConventions(out string? error) => LoadTurnPathConventions(out error, out _);

        /// <summary>
        /// The turning path conventions, or null with the reason. <paramref name="notes"/> says what
        /// was changed in the file as it was read: cars turned the right way round, vehicles left out.
        /// </summary>
        public TurnPathConventions? LoadTurnPathConventions(out string? error, out List<string> notes)
        {
            string path = ResolvedTurnPathsPath();
            error = null;
            notes = new List<string>();
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

                // A copy saved before the B85 and B99 were corrected has them back to front.
                List<string> corrected = conventions.CorrectReversedCars();
                if (corrected.Count > 0)
                {
                    notes.Add($"{string.Join(" and ", corrected)}: front and rear overhangs were the wrong way round in {path} and have been corrected to AS/NZS 2890.1 (front the shorter). " +
                              "Paths drawn before will change when edited. Save as defaults in the dialog to keep the correction.");
                }

                // Vehicles with trailers are switched off for now.
                List<string> towing = conventions.RemoveVehiclesWithTrailers();
                if (towing.Count > 0)
                {
                    notes.Add($"vehicles with trailers are switched off for now, so {string.Join(", ", towing)} {(towing.Count == 1 ? "is" : "are")} not offered.");
                }

                if (conventions.Vehicles.Count == 0)
                {
                    error = $"{path} has no vehicles without trailers";
                    return null;
                }

                if (!conventions.Vehicles.ContainsKey(conventions.DefaultVehicle ?? ""))
                {
                    conventions.DefaultVehicle = conventions.Vehicles.Keys.First();
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
