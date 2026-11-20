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
    /// Per-user settings in %APPDATA%\PointerPath\settings.json: where a shared copy of the
    /// turning path conventions is (optional), and what was last chosen in the dialogs that
    /// remember their choices. Also where the conventions themselves are found.
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

        /// <summary>
        /// What was last chosen in the dialogs that remember their choices (Drive and Draw
        /// settings). Laid over the conventions each time they are loaded.
        /// </summary>
        [JsonPropertyName("last_used")]
        public LastUsed? LastUsed { get; set; }

        /// <summary>Anything else in the file, kept as it is when the file is saved.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Other { get; set; }

        public static PointerPathSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    return JsonSerializer.Deserialize<PointerPathSettings>(File.ReadAllText(SettingsPath), TurnPathConventions.JsonOptions) ?? new PointerPathSettings();
                }
            }
            catch (Exception)
            {
                // An unreadable settings file falls back to the defaults.
            }

            return new PointerPathSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, TurnPathConventions.JsonOptions));
        }

        /// <summary>
        /// Keeps the Drive dialog's choices in <paramref name="conventions"/> (vehicle, conflict
        /// check, wheels, units) as the ones to start from next time. They go in the user's
        /// settings.json, not in turn-paths.json: that file, which may be a shared one, stays the
        /// vehicles and the office's standards.
        /// </summary>
        public static void RememberDrive(TurnPathConventions conventions)
        {
            PointerPathSettings settings = Load();
            LastUsed last = settings.LastUsed ?? (settings.LastUsed = new LastUsed());
            last.Vehicle = conventions.DefaultVehicle;
            last.DetectConflicts = conventions.CheckConflicts;
            last.TurnWheelsWhenStationary = conventions.DriveTurnWheelsWhenStationary;
            last.DrawingUnits = conventions.DrawingUnits;
            settings.Save();
        }

        /// <summary>
        /// Keeps what the Draw settings dialog was OK'd with: what to draw, and the layers that were
        /// changed there (the rest still follow turn-paths.json).
        /// </summary>
        public static void RememberDrawSettings(TurnPathDrawOptions draw, IEnumerable<KeyValuePair<string, string>> changedLayers)
        {
            PointerPathSettings settings = Load();
            LastUsed last = settings.LastUsed ?? (settings.LastUsed = new LastUsed());
            last.Draw = draw.Clone();
            foreach (KeyValuePair<string, string> kv in changedLayers)
            {
                (last.Layers ?? (last.Layers = new Dictionary<string, string>()))[kv.Key] = kv.Value;
            }

            settings.Save();
        }

        /// <summary>
        /// Drops what was last chosen. The earlier dialogs do this on Save as defaults: the
        /// conventions they have just written are then what every dialog starts from.
        /// </summary>
        public static void ForgetLastUsed()
        {
            PointerPathSettings settings = Load();
            if (settings.LastUsed != null)
            {
                settings.LastUsed = null;
                settings.Save();
            }
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

                LastUsed?.ApplyTo(conventions);

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

    /// <summary>
    /// The choices the Drive and Draw settings dialogs remember from one run to the next, so
    /// the dialog opens as it was left and Quick drive can go without one. Anything not set
    /// here comes from the conventions; see <see cref="PointerPathSettings.RememberDrive"/> and
    /// <see cref="PointerPathSettings.RememberDrawSettings"/>.
    /// </summary>
    internal sealed class LastUsed
    {
        public string? Vehicle { get; set; }

        public bool? DetectConflicts { get; set; }

        public bool? TurnWheelsWhenStationary { get; set; }

        /// <summary>"auto", "m", "mm", "in" or "ft".</summary>
        public string? DrawingUnits { get; set; }

        public TurnPathDrawOptions? Draw { get; set; }

        /// <summary>The layers changed in Draw settings, by part as in turn-paths.json ("swept_path", "clearance", ...).</summary>
        public Dictionary<string, string>? Layers { get; set; }

        public void ApplyTo(TurnPathConventions c)
        {
            // A vehicle that has since gone from the conventions leaves their default standing.
            string? vehicle = c.Vehicles.Keys.FirstOrDefault(k => string.Equals(k, Vehicle, StringComparison.OrdinalIgnoreCase));
            if (vehicle != null)
            {
                c.DefaultVehicle = vehicle;
            }

            c.CheckConflicts = DetectConflicts ?? c.CheckConflicts;
            c.DriveTurnWheelsWhenStationary = TurnWheelsWhenStationary ?? c.DriveTurnWheelsWhenStationary;
            if (!string.IsNullOrWhiteSpace(DrawingUnits))
            {
                c.DrawingUnits = DrawingUnits!;
            }

            if (Draw != null)
            {
                c.Draw = Draw.Clone();
            }

            foreach (KeyValuePair<string, string> kv in Layers ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                {
                    c.Layers.Set(kv.Key, kv.Value);
                }
            }
        }
    }
}
