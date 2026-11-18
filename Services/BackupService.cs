using System.IO;
using System.Text.Json;
using WindowsAppManager.Models;

namespace WindowsAppManager.Services;

public sealed class BackupService(AppStore store, string scoopRoot)
{
    public sealed class Profile
    {
        public string[] AppNames { get; set; } = [];
        public string[] PackageIds { get; set; } = [];
        public List<ProfileFile> Files { get; set; } = [];
    }
    public sealed class ProfileFile
    {
        public string Name { get; set; } = "";
        public string[] Candidates { get; set; } = [];
        public bool Optional { get; set; }
    }
    public sealed class BackupEntry
    {
        public string FileName { get; set; } = "";
        public string Source { get; set; } = "";
        public string RelativeTarget { get; set; } = "";
        public long Bytes { get; set; }
    }
    public sealed class BackupMetadata
    {
        public string AppName { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string Version { get; set; } = "";
        public string ManagedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<BackupEntry> Files { get; set; } = [];
    }

    public Profile? GetProfile(AppItem app)
    {
        string[] roots = [store.ProfilesPath, Path.Combine(AppContext.BaseDirectory, "BackupProfiles")];
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), new JsonSerializerOptions
                    { PropertyNameCaseInsensitive = true });
                    if (p != null && (p.PackageIds.Any(x => x.Equals(app.PackageId, StringComparison.OrdinalIgnoreCase))
                        || p.AppNames.Any(x => x.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))) return p;
                }
                catch { /* Malformed profiles are skipped. */ }
            }
        }
        return null;
    }

    public async Task<(string Path, int Files)> BackupAsync(AppItem app, IReadOnlyList<string>? manualPaths = null)
    {
        var profile = GetProfile(app);
        if (manualPaths is null && profile == null) throw new InvalidOperationException("Không có backup profile cho app này.");
        // Collect sources before creating the backup folder.
        var paths = new List<(string Name, string Source)>();
        if (manualPaths != null)
        {
            foreach (string source in manualPaths)
                if (File.Exists(source) && !paths.Any(x => x.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
                    paths.Add((Path.GetFileName(source), source));
        }
        else
        {
            foreach (var rule in profile!.Files)
            {
                string? existing = rule.Candidates.Select(p => Expand(p, app))
                    .FirstOrDefault(File.Exists);
                if (existing != null) paths.Add((rule.Name, existing));
                else if (!rule.Optional) throw new FileNotFoundException($"Không tìm thấy file backup bắt buộc: {rule.Name}");
            }
        }
        if (paths.Count == 0) throw new FileNotFoundException("Không tìm thấy file nào để sao lưu.");
        string safeName = new string(app.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        string dest = Path.Combine(store.BackupRoot, safeName, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff"));
        string dataDir = Path.Combine(dest, "files");
        Directory.CreateDirectory(dataDir);
        var meta = new BackupMetadata { AppName = app.Name, PackageId = app.PackageId,
            Version = app.Version, ManagedBy = app.ManagedBy };
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                var item = paths[i];
                string relative = $"{i:000}_{Path.GetFileName(item.Name)}";
                string target = Path.Combine(dataDir, relative);
                await using var src = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await src.CopyToAsync(dst);
                meta.Files.Add(new BackupEntry { FileName = item.Name, Source = item.Source,
                    RelativeTarget = Path.Combine("files", relative), Bytes = src.Length });
            }
            await File.WriteAllTextAsync(Path.Combine(dest, "backup-info.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            return (dest, meta.Files.Count);
        }
        catch
        {
            try { Directory.Delete(dest, true); } catch { }
            throw;
        }
    }

    private string Expand(string template, AppItem app)
    {
        string scoopPersist = Path.Combine(scoopRoot, "persist", app.PackageId);
        return template.Replace("{InstallPath}", app.InstallPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{ScoopPersist}", scoopPersist, StringComparison.OrdinalIgnoreCase)
            .Replace("{AppData}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase);
    }
}
