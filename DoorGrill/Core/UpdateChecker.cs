using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace DoorGrill
{
    internal class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
    }

    internal static class UpdateChecker
    {
        // Raw URL to version.json in the public releases repository.
        private const string VersionUrl =
            "https://raw.githubusercontent.com/ioavant/DoorGrill-releases/main/version.json";

        public static UpdateInfo AvailableUpdate { get; private set; }

        /// <summary>Version of the running build, for display ("1.0.0").</summary>
        public static string CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "" : v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static void CheckInBackground()
        {
            // TES builds are distributed internally and have no public release feed, so the whole
            // mechanism stays dormant: no request, and AvailableUpdate stays null, which keeps the
            // update banner out of the Options dialog.
            if (!Brand.UpdateCheckEnabled) return;

            Task.Run(() =>
            {
                try { DoCheck(); }
                catch { /* network unavailable, or nothing published yet - ignore */ }
            });
        }

        private static void DoCheck()
        {
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "DoorGrill-UpdateChecker/1.0";
                string json = client.DownloadString(VersionUrl);
                UpdateInfo info = ParseJson(json);
                if (info == null || info.Version == null) return;

                Version current = Assembly.GetExecutingAssembly().GetName().Version;
                Version remote;
                if (Version.TryParse(info.Version, out remote) && remote > current)
                    AvailableUpdate = info;
            }
        }

        // Minimal hand-rolled JSON value extractor - no external dependencies needed for three keys.
        private static UpdateInfo ParseJson(string json)
        {
            try
            {
                return new UpdateInfo
                {
                    Version = ExtractString(json, "version"),
                    DownloadUrl = ExtractString(json, "downloadUrl"),
                    ReleaseNotes = ExtractString(json, "releaseNotes") ?? string.Empty
                };
            }
            catch { return null; }
        }

        private static string ExtractString(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return null;
            int open = json.IndexOf('"', colon);
            if (open < 0) return null;
            int close = json.IndexOf('"', open + 1);
            if (close < 0) return null;
            return json.Substring(open + 1, close - open - 1);
        }
    }
}
