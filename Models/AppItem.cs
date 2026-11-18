namespace WindowsAppManager.Models;

public sealed class AppItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string InstalledVia { get; set; } = "Unknown";
    public string ManagedBy { get; set; } = "Manual";
    public string InstallPath { get; set; } = "";
    public string Version { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public long? SizeBytes { get; set; }
    public bool SizeIsPartial { get; set; }
    public DateTime? InstallDate { get; set; }
    public bool IsInstalled { get; set; }
    public bool IsPlanned { get; set; }
    public bool UpdateAvailable { get; set; }
    public string Scope { get; set; } = "";
    public string Status => !IsInstalled ? "Not installed" : UpdateAvailable ? "Update available" : "Installed";
    public string SizeText => SizeBytes is null ? "—" : $"{(SizeIsPartial ? "≥ " : "")}{SizeBytes.Value / 1048576.0:N1} MB";
    public string DateText => InstallDate?.ToString("yyyy-MM-dd") ?? "—";
    public string PathText => string.IsNullOrWhiteSpace(InstallPath) ? "Not reported" : InstallPath;
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;
}

public sealed class PlannedApp
{
    public string Name { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string ManagedBy { get; set; } = "WinGet";
    public string InstallPath { get; set; } = "";
}

public sealed class AppState
{
    public List<PlannedApp> Planned { get; set; } = [];
    public Dictionary<string, GithubInstallation> GithubInstallations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // An installation's actual original installer often cannot be inferred later.
    // We only set this after an action taken through this app.
    public Dictionary<string, string> InstallationHistory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GithubInstallation
{
    public string RootPath { get; set; } = "";
    public string VersionPath { get; set; } = "";
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
}
