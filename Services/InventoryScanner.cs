using System.IO;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindowsAppManager.Models;

namespace WindowsAppManager.Services;

public sealed class InventoryScanner(AppStore store)
{
    public string ScoopRoot { get; private set; } = FindScoopRoot(store.State.ScoopRoot);

    public void SetScoopRoot(string root) => ScoopRoot = root;

    public static string FindScoopRoot(string? configured = null)
    {
        string? environment = Environment.GetEnvironmentVariable("SCOOP");
        var paths = new[]
        {
            configured, environment, Environment.GetEnvironmentVariable("SCOOP", EnvironmentVariableTarget.User), @"D:\PortableApps\Scoop",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop")
        };
        return paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)
            && ScoopSetup.IsInstalled(p!)) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop");
    }

    public async Task<(List<AppItem> Apps, List<string> Warnings)> ScanAsync()
    {
        var warnings = new List<string>();
        var registryTask = Task.Run(ScanRegistry);
        var scoopTask = Task.Run(ScanScoop);
        CommandResult? winget = null;
        try { winget = await CommandRunner.WinGetAsync("list", "--accept-source-agreements", "--disable-interactivity"); }
        catch (Exception e) { warnings.Add("WinGet: " + e.Message); }
        List<AppItem> apps = await registryTask;
        apps.AddRange(await scoopTask);
        if (winget is { Succeeded: true }) MergeWinGet(apps, winget.Output);
        else if (winget is not null) warnings.Add("WinGet list thất bại: " + winget.Output);
        ApplyPlanned(apps);
        return (apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.InstallPath).ToList(), warnings);
    }

    private List<AppItem> ScanRegistry()
    {
        List<AppItem> apps = [];
        var sources = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, "Machine"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "Machine"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, "User"),
            (RegistryHive.CurrentUser, RegistryView.Registry32, "User")
        };
        foreach (var (hive, view, scope) in sources)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key == null || key.GetValue("SystemComponent")?.ToString() == "1") continue;
                    string title = key.GetValue("DisplayName")?.ToString()?.Trim() ?? "";
                    if (title.Length == 0) continue;
                    string version = key.GetValue("DisplayVersion")?.ToString() ?? "";
                    string location = key.GetValue("InstallLocation")?.ToString()?.Trim().Trim('"') ?? "";
                    if (string.IsNullOrEmpty(location)) location = InferFromIcon(key.GetValue("DisplayIcon")?.ToString());
                    string id = $"registry:{hive}:{view}:{name}";
                    long? size = null;
                    if (long.TryParse(key.GetValue("EstimatedSize")?.ToString(), out long kb) && kb > 0) size = kb * 1024L;
                    DateTime? date = DateTime.TryParseExact(key.GetValue("InstallDate")?.ToString(), "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var d) ? d : null;
                    // Registry exposes the installation but not the original tool used to install it.
                    apps.Add(new AppItem { Id = id, Name = title, Version = version, InstallPath = location,
                        InstalledVia = "Unknown", ManagedBy = "Manual", Scope = scope,
                        SizeBytes = size, InstallDate = date, IsInstalled = true });
                }
            }
            catch { /* Restricted / removed registry keys are skipped. */ }
        }
        // Registry32/64 aliases can expose identical entries. Different real paths remain separate.
        return apps.GroupBy(a => string.Join("|", Normalize(a.Name), a.Version,
                    !string.IsNullOrEmpty(a.InstallPath) ? a.InstallPath.ToLowerInvariant() : a.Id.Split(':').Last(), a.Scope),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.SizeBytes.HasValue).First()).ToList();
    }

    private static string InferFromIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return "";
        string candidate = icon.Trim().Trim('"');
        int suffix = candidate.LastIndexOf(',');
        if (suffix >= 0 && int.TryParse(candidate[(suffix + 1)..], out _)) candidate = candidate[..suffix].Trim('"');
        if (File.Exists(candidate)) return Path.GetDirectoryName(candidate) ?? "";
        return "";
    }

    private List<AppItem> ScanScoop()
    {
        List<AppItem> results = [];
        string root = Path.Combine(ScoopRoot, "apps");
        if (!Directory.Exists(root)) return results;
        foreach (string appPath in Directory.EnumerateDirectories(root))
        {
            string package = Path.GetFileName(appPath);
            if (package.Equals("scoop", StringComparison.OrdinalIgnoreCase)) continue;
            string current = Path.Combine(appPath, "current");
            if (!Directory.Exists(current)) continue;
            string version = "";
            try
            {
                // A folder under Scoop/apps is not proof that Scoop installed it.
                if (File.Exists(Path.Combine(current, ".wam-installed.json"))) continue;
                string receipt = ScoopMetadata.ReceiptPath(current);
                string scoopManifest = ScoopMetadata.ManifestPath(current);
                if (!File.Exists(receipt) || !File.Exists(scoopManifest)) continue;
                using (var receiptDoc = JsonDocument.Parse(File.ReadAllText(receipt)))
                    if (receiptDoc.RootElement.ValueKind != JsonValueKind.Object) continue;
                using (var manifestDoc = JsonDocument.Parse(File.ReadAllText(scoopManifest)))
                    if (!manifestDoc.RootElement.TryGetProperty("version", out _)) continue;
                var dir = new DirectoryInfo(current);
                if (!string.IsNullOrEmpty(dir.LinkTarget)) version = Path.GetFileName(dir.LinkTarget.TrimEnd('\\','/'));
                string manifest = scoopManifest;
                if (File.Exists(manifest))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (doc.RootElement.TryGetProperty("version", out var v)) version = v.GetString() ?? version;
                }
                if (string.IsNullOrEmpty(version))
                    version = Directory.EnumerateDirectories(appPath).Select(Path.GetFileName)
                        .Where(n => n != "current" && !string.IsNullOrEmpty(n))
                        .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "";
                var size = EstimateDirectorySize(current);
                results.Add(new AppItem { Id = "scoop:" + package, Name = package, PackageId = package,
                    Version = version, InstalledVia = "Scoop", ManagedBy = "Scoop", InstallPath = current,
                    IsInstalled = true, Scope = "User", SizeBytes = size.Bytes, SizeIsPartial = size.Partial,
                    InstallDate = Directory.GetCreationTime(appPath) });
            }
            catch { /* Partial installs may lack a manifest. */ }
        }
        return results;
    }

    private static (long? Bytes, bool Partial) EstimateDirectorySize(string root)
    {
        try
        {
            long sum = 0;
            int files = 0;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string path = pending.Pop();
                foreach (string file in Directory.EnumerateFiles(path))
                {
                    if (++files > 5000) return (sum, true);
                    try { sum += new FileInfo(file).Length; } catch { }
                }
                foreach (string dir in Directory.EnumerateDirectories(path))
                {
                    try { if (!File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) pending.Push(dir); }
                    catch { }
                }
            }
            return (sum, false);
        }
        catch { return (null, false); }
    }

    // Winget's text output is not a stable machine API. Use conservative matching.
    // A future version can replace this with Microsoft.WinGet.Client structured APIs.
    private void MergeWinGet(List<AppItem> apps, string output)
    {
        var rows = ParseWingetList(output);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id)) continue;
            bool isMsix = row.Id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase);
            var candidates = apps.Where(a => a.ManagedBy != "Scoop" && a.IsInstalled
                    && Normalize(a.Name) == Normalize(row.Name)).ToList();
            if (candidates.Count == 1)
            {
                var target = candidates[0];
                target.ManagedBy = isMsix ? "MSIX" : "WinGet";
                target.PackageId = row.Id;
                target.LatestVersion = row.Available;
                target.UpdateAvailable = false;
                if (store.State.InstallationHistory.TryGetValue("WinGet:" + row.Id, out string? recorded))
                    target.InstalledVia = recorded;
            }
            else if (candidates.Count == 0)
            {
                // Could be an MSIX app not listed in the traditional uninstall registry.
                apps.Add(new AppItem { Id = "winget:" + row.Id, Name = row.Name, PackageId = row.Id,
                    Version = row.Version, LatestVersion = row.Available,
                    UpdateAvailable = false,
                    ManagedBy = isMsix ? "MSIX" : "WinGet", InstalledVia = "Unknown", IsInstalled = true });
            }
            // Ambiguous registry matches are left alone rather than guessing an install to control.
        }
    }

    private record WinGetRow(string Name, string Id, string Version, string Available);
    private static List<WinGetRow> ParseWingetList(string output)
    {
        List<WinGetRow> rows = [];
        bool inTable = false;
        int idIndex = 1, versionIndex = 2, availableIndex = -1;
        foreach (string line in output.Split('\n'))
        {
            string clean = Regex.Replace(line, @"\x1B\[[0-9;]*[A-Za-z]", "").Trim();
            if (clean.Length == 0) continue;
            if (!inTable)
            {
                string[] headers = Regex.Split(clean, @"\s{2,}");
                if (headers.Length >= 3 && headers[0].Equals("Name", StringComparison.OrdinalIgnoreCase)
                    && headers.Any(h => h.Equals("Id", StringComparison.OrdinalIgnoreCase)))
                {
                    inTable = true;
                    idIndex = Array.FindIndex(headers, h => h.Equals("Id", StringComparison.OrdinalIgnoreCase));
                    versionIndex = Array.FindIndex(headers, h => h.Equals("Version", StringComparison.OrdinalIgnoreCase));
                    availableIndex = Array.FindIndex(headers, h => h.Equals("Available", StringComparison.OrdinalIgnoreCase));
                }
                continue;
            }
            if (clean.StartsWith("---") || clean.StartsWith("The following") || clean.StartsWith("No installed")) continue;
            string[] pieces = Regex.Split(clean, @"\s{2,}");
            if (idIndex < 0 || pieces.Length <= Math.Max(idIndex, versionIndex)) continue;
            string id = pieces[idIndex];
            // Reject fragments due to column truncation, and only map plausible ids.
            if (id.Contains(' ') || id.Length < 2 || id == "—") continue;
            rows.Add(new WinGetRow(pieces[0], id, versionIndex >= 0 ? pieces[versionIndex] : "",
                availableIndex >= 0 && pieces.Length > availableIndex ? pieces[availableIndex] : ""));
        }
        return rows;
    }

    private void ApplyPlanned(List<AppItem> apps)
    {
        foreach (var planned in store.State.Planned)
        {
            var matches = apps.Where(a => a.IsInstalled
                && a.ManagedBy.Equals(planned.ManagedBy, StringComparison.OrdinalIgnoreCase)
                && (a.PackageId.Equals(planned.PackageId, StringComparison.OrdinalIgnoreCase)
                  || (planned.ManagedBy == "Manual" && a.Name.Equals(planned.Name, StringComparison.OrdinalIgnoreCase)
                      && a.InstallPath.Equals(planned.InstallPath, StringComparison.OrdinalIgnoreCase)))).ToList();
            if (matches.Count > 0)
            {
                foreach (var m in matches) m.IsPlanned = true;
                continue;
            }
            // Manual portable programs may not register in Windows Installed Apps.
            // The user may still track a specific existing local folder/file explicitly.
            store.State.GithubInstallations.TryGetValue(planned.PackageId, out var github);
            bool githubPresent = planned.ManagedBy == "GitHub" && github != null && Directory.Exists(github.Path)
                && File.Exists(Path.Combine(github.Path, ".wam-installed.json"));
            bool manualPresent = planned.ManagedBy == "Manual" &&
                (Directory.Exists(planned.InstallPath) || File.Exists(planned.InstallPath));
            apps.Add(new AppItem { Id = $"planned:{planned.ManagedBy}:{planned.PackageId}:{planned.InstallPath}",
                Name = planned.Name, PackageId = planned.PackageId, ManagedBy = planned.ManagedBy,
                InstallPath = githubPresent ? github!.Path : planned.InstallPath, InstalledVia = githubPresent ? "GitHub" : manualPresent ? "Manual" : "—",
                Version = githubPresent ? github!.Version : "", IsInstalled = manualPresent || githubPresent, IsPlanned = true });
        }
    }

    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
}
