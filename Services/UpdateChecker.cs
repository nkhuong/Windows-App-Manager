using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;
using WindowsAppManager.Models;

namespace WindowsAppManager.Services;

public static class UpdateChecker
{
    public static string LastDetails { get; private set; } = "";
    public static async Task<List<string>> CheckAsync(List<AppItem> apps, string scoopRoot)
    {
        List<string> warnings = [];
        var details = new StringBuilder();
        foreach (var app in apps) { app.UpdateAvailable = false; app.LatestVersion = ""; }
        try
        {
            var result = await CommandRunner.WinGetAsync("upgrade", "--accept-source-agreements", "--disable-interactivity");
            details.AppendLine("WINGET (live query)\nCOMMAND (PowerShell syntax):\n" + result.CommandLine + "\n\n" + result.Output);
            // WinGet returns this code when there are no applicable upgrades.
            if (!result.Succeeded && result.ExitCode != PackageActionFeedback.WinGetUpdateNotApplicable)
                warnings.Add("WinGet update check failed (" + result.ExitCode + ").");
            else if (result.Succeeded && !ApplyTable(apps, result.Output, "WinGet", "Id", "Version", "Available")
                && !result.Output.Contains("No installed package", StringComparison.OrdinalIgnoreCase)
                && !result.Output.Contains("No applicable upgrade", StringComparison.OrdinalIgnoreCase))
                warnings.Add("WinGet update output was not recognized.");
        }
        catch (Exception ex) { warnings.Add("WinGet updates: " + ex.Message); }
        // Read JSON manifests directly: Scoop status text varies across versions and locales.
        details.AppendLine("\nSCOOP (local bucket manifests; Refresh does not fetch buckets)");
        foreach (var app in apps.Where(a => a.IsInstalled && a.ManagedBy == "Scoop"))
        {
            try
            {
                string install = ScoopMetadata.ReceiptPath(app.InstallPath);
                string? bucket = null;
                bool held = false;
                if (File.Exists(install))
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(install));
                    if (doc.RootElement.TryGetProperty("bucket", out var b)) bucket = b.GetString();
                    held = doc.RootElement.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.True;
                }
                if (held) { details.AppendLine(app.Name + ": held; skipped"); continue; }
                string? manifest = null;
                if (!string.IsNullOrEmpty(bucket) && Path.GetFileName(bucket) == bucket)
                {
                    string candidate = Path.Combine(scoopRoot, "buckets", bucket, "bucket", app.PackageId + ".json");
                    if (File.Exists(candidate)) manifest = candidate;
                }
                else
                {
                    string buckets = Path.Combine(scoopRoot, "buckets");
                    List<string> candidates = Directory.Exists(buckets) ? Directory.EnumerateDirectories(buckets)
                        .Select(b => Path.Combine(b, "bucket", app.PackageId + ".json")).Where(File.Exists).ToList() : [];
                    if (candidates.Count == 1) manifest = candidates[0];
                }
                if (manifest == null) { warnings.Add("Scoop: no matching bucket manifest for " + app.PackageId); continue; }
                using var latestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
                string latest = latestDoc.RootElement.GetProperty("version").GetString() ?? "";
                app.LatestVersion = latest;
                // Scoop itself treats unequal manifest versions as an available update (including nightly).
                app.UpdateAvailable = latest.Length > 0 && latest != "nightly"
                    && !latest.Equals(app.Version, StringComparison.OrdinalIgnoreCase);
                details.AppendLine($"{app.PackageId}: {app.Version} -> {latest}" + (app.UpdateAvailable ? " (update)" : ""));
            }
            catch (Exception ex) { warnings.Add("Scoop " + app.PackageId + ": " + ex.Message); }
        }
        LastDetails = details.ToString();
        return warnings;
    }

    private static bool ApplyTable(List<AppItem> apps, string output, string provider,
        string idHeader, string installedHeader, string latestHeader)
    {
        // Slice fixed-width columns: splitting spaces moves Source into an empty Available column.
        List<(string Name, int Start)> columns = [];
        bool recognized = false;
        foreach (var raw in output.Split('\n'))
        {
            string line = Regex.Replace(raw.TrimEnd('\r'), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            var tokens = Regex.Matches(line, @"\S+").Cast<Match>().ToList();
            if (tokens.Any(x => x.Value == idHeader) && tokens.Any(x => x.Value == installedHeader)
                && tokens.Any(x => x.Value == latestHeader))
            {
                recognized = true;
                columns = tokens.Select(x => (x.Value, x.Index)).ToList();
                continue;
            }
            if (columns.Count == 0 || string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("---")) continue;
            string Read(string header)
            {
                int index = columns.FindIndex(x => x.Name == header);
                if (index < 0 || columns[index].Start >= line.Length) return "";
                int start = columns[index].Start;
                int end = index + 1 < columns.Count ? Math.Min(line.Length, columns[index + 1].Start) : line.Length;
                return line[start..end].Trim();
            }
            string id = Read(idHeader), installed = Read(installedHeader), latest = Read(latestHeader);
            if (id.Length == 0 || id.Contains('…') || id.Any(char.IsWhiteSpace)
                || latest.Length == 0 || latest is "—" or "-" || installed == latest) continue;
            foreach (var app in apps.Where(a => a.IsInstalled && a.ManagedBy == provider
                && a.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                app.UpdateAvailable = true;
                app.LatestVersion = latest;
            }
        }
        return recognized;
    }
}
