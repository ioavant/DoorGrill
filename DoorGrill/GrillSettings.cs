using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace DoorGrill
{
    // The options the user edits in the Options dialog.
    //
    // Stored in a small JSON file NEXT TO THE DLL - i.e. in the install folder,
    // %ProgramData%\<brand>\DoorGrill\ - so one set of options serves every Revit version on the
    // machine, and every project. (Deliberately not per project: the grille family and the mounting
    // height are how this office works, not a property of one model.)
    //
    // ProgramData grants BUILTIN\Users write access to subfolders, so no elevation is needed. A file
    // created there by another user can still be unwritable, so saving falls back to a per-user copy
    // under %AppData%, which then takes precedence when loading.
    internal class GrillSettings
    {
        private const string FileName = "DoorGrill.settings.json";

        public const double DefaultGapMm = 100.0;

        /// <summary>Family name of the grille to place. Empty until the user picks one.</summary>
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>Clear distance from the door head up to the BOTTOM of the grille, in millimetres.</summary>
        public double MountingGapMm { get; set; } = DefaultGapMm;

        /// <summary>Turn the grille around: face the door's swing side instead of away from it.</summary>
        public bool FlipOrientation { get; set; }

        /// <summary>
        /// When true, mode 2 leaves a door alone once its grille has been deleted by hand. Off by
        /// default: a plain run of mode 2 restores every missing grille, which is what most people
        /// expect from a "regenerate" command.
        /// </summary>
        public bool KeepDeletedDoorsEmpty { get; set; }

        public bool HasFamily
        {
            get { return !string.IsNullOrEmpty(FamilyName) && FamilyName.Trim().Length > 0; }
        }

        /// <summary>The mounting gap in Revit's internal units.</summary>
        public double MountingGapFt
        {
            get { return MountingGapMm * GrillPlacer.MM_TO_FT; }
        }

        public void SetMountingGapFt(double feet)
        {
            MountingGapMm = feet / GrillPlacer.MM_TO_FT;
        }

        // ── Locations ─────────────────────────────────────────────────────────

        private static string SharedPath
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
                }
                catch { return null; }
            }
        }

        private static string UserPath
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        Brand.DisplayName, Brand.ProductName, FileName);
                }
                catch { return null; }
            }
        }

        /// <summary>Where the options are currently kept, for display in the dialog.</summary>
        public static string CurrentLocation
        {
            get
            {
                string user = UserPath;
                if (user != null && File.Exists(user)) return user;
                return SharedPath ?? user ?? "";
            }
        }

        // ── Load / Save ───────────────────────────────────────────────────────

        /// <summary>The stored options, or the defaults. Never throws.</summary>
        public static GrillSettings Load()
        {
            GrillSettings s = new GrillSettings();
            s.ReadFrom(SharedPath);
            s.ReadFrom(UserPath); // a per-user copy exists only as a fallback, so it wins
            return s;
        }

        /// <summary>
        /// Write the options next to the DLL, falling back to a per-user copy when that folder is not
        /// writable for this account. False only if both attempts failed.
        /// </summary>
        public bool Save()
        {
            return TryWrite(SharedPath) || TryWrite(UserPath);
        }

        private bool TryWrite(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, ToJson(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ReadFrom(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string json;
            try
            {
                if (!File.Exists(path)) return;
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch { return; }

            string family = Value(json, "familyName");
            if (family != null) FamilyName = family;

            double gap;
            string gapText = Value(json, "mountingGapMm");
            if (gapText != null &&
                double.TryParse(gapText, NumberStyles.Any, CultureInfo.InvariantCulture, out gap) && gap >= 0.0)
                MountingGapMm = gap;

            FlipOrientation = Bool(json, "flipOrientation", FlipOrientation);
            KeepDeletedDoorsEmpty = Bool(json, "keepDeletedDoorsEmpty", KeepDeletedDoorsEmpty);
        }

        private string ToJson()
        {
            return "{\n"
                 + "  \"familyName\": \"" + Escape(FamilyName) + "\",\n"
                 + "  \"mountingGapMm\": " + MountingGapMm.ToString("0.###", CultureInfo.InvariantCulture) + ",\n"
                 + "  \"flipOrientation\": " + (FlipOrientation ? "true" : "false") + ",\n"
                 + "  \"keepDeletedDoorsEmpty\": " + (KeepDeletedDoorsEmpty ? "true" : "false") + "\n"
                 + "}\n";
        }

        // ── Minimal JSON reading ──────────────────────────────────────────────
        // Four flat keys do not justify a JSON dependency in a Revit add-in. Reads one value token:
        // a quoted string (unescaped) or a bare number / true / false.

        private static string Value(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return null;

            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;

            if (json[i] == '"')
            {
                StringBuilder sb = new StringBuilder();
                i++;
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\' && i + 1 < json.Length)
                    {
                        i++;
                        sb.Append(json[i] == 'n' ? '\n' : json[i] == 't' ? '\t' : json[i]);
                    }
                    else sb.Append(json[i]);
                    i++;
                }
                return sb.ToString();
            }

            int end = i;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n') end++;
            return json.Substring(i, end - i).Trim();
        }

        private static bool Bool(string json, string key, bool fallback)
        {
            string v = Value(json, key);
            if (v == null) return fallback;
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
