using System.IO;
using System.Text.Json;
using WindowsAppManager.Models;

namespace WindowsAppManager.Services;

public sealed class AppStore
{
    public string DirectoryPath { get; }
    public string BackupRoot => Path.Combine(DirectoryPath, "Backups");
    public string ProfilesPath => Path.Combine(DirectoryPath, "BackupProfiles");
    public string StatePath => Path.Combine(DirectoryPath, "state.json");
    public AppState State { get; private set; } = new();

    public AppStore()
    {
        string preferred = @"D:\AppConfig\WindowsAppManager";
        DirectoryPath = Directory.Exists(@"D:\")
            ? preferred
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsAppManager");
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(BackupRoot);
        Directory.CreateDirectory(ProfilesPath);
        try
        {
            if (File.Exists(StatePath))
                State = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath)) ?? new();
        }
        catch { State = new(); }
        State.Planned ??= [];
        State.GithubInstallations = new(State.GithubInstallations ?? new(), StringComparer.OrdinalIgnoreCase);
        State.InstallationHistory = new Dictionary<string, string>(State.InstallationHistory ?? new(), StringComparer.OrdinalIgnoreCase);
    }

    public void Save()
    {
        string tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, StatePath, true);
    }

    public void AddPlanned(PlannedApp app)
    {
        if (State.Planned.Any(p => p.ManagedBy.Equals(app.ManagedBy, StringComparison.OrdinalIgnoreCase)
            && p.PackageId.Equals(app.PackageId, StringComparison.OrdinalIgnoreCase)
            && p.InstallPath.Equals(app.InstallPath, StringComparison.OrdinalIgnoreCase))) return;
        State.Planned.Add(app);
        Save();
    }

    public void RemovePlanned(PlannedApp app)
    {
        State.Planned.RemoveAll(p => p.ManagedBy.Equals(app.ManagedBy, StringComparison.OrdinalIgnoreCase)
            && p.PackageId.Equals(app.PackageId, StringComparison.OrdinalIgnoreCase)
            && p.InstallPath.Equals(app.InstallPath, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void ReplacePlanned(PlannedApp oldApp, PlannedApp replacement)
    {
        State.Planned.RemoveAll(p => p.ManagedBy.Equals(oldApp.ManagedBy, StringComparison.OrdinalIgnoreCase)
            && p.PackageId.Equals(oldApp.PackageId, StringComparison.OrdinalIgnoreCase)
            && p.InstallPath.Equals(oldApp.InstallPath, StringComparison.OrdinalIgnoreCase));
        if (!State.Planned.Any(p => p.ManagedBy.Equals(replacement.ManagedBy, StringComparison.OrdinalIgnoreCase)
            && p.PackageId.Equals(replacement.PackageId, StringComparison.OrdinalIgnoreCase)
            && p.InstallPath.Equals(replacement.InstallPath, StringComparison.OrdinalIgnoreCase)))
            State.Planned.Add(replacement);
        Save();
    }

    public void RecordInstalled(string provider, string packageId)
    {
        State.InstallationHistory[$"{provider}:{packageId}"] = provider;
        Save();
    }
}
