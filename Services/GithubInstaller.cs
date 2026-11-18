using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace WindowsAppManager.Services;

public sealed record GithubAsset(string Name, string Url, string? Digest);
public sealed record GithubRelease(string Version, List<GithubAsset> Assets);

public static class GithubInstaller
{
    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsAppManager/0.1.4");
        return client;
    }
    private static string RepositoryPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$"))
            throw new InvalidOperationException("Enter a GitHub repository URL: https://github.com/owner/repo");
        return uri.AbsolutePath.Trim('/');
    }
    public static string RepositoryName(string url) => RepositoryPath(url).Split('/')[1];
    public static string VersionFolder(string version, string assetName)
    {
        string label = System.Text.RegularExpressions.Regex.Replace(version, @"[^A-Za-z0-9._-]", "_").Trim('.', ' ');
        if (label.Length == 0) label = "release";
        if (label.Length > 60) label = label[..60];
        string suffix = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assetName)))[..8].ToLowerInvariant();
        return "release-" + label + "-" + suffix;
    }

    public static async Task CreateCurrentAsync(string current, string target)
    {
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
        if (Directory.Exists(current) || File.Exists(current)) throw new IOException("Current path already exists.");
        string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        string script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path "
            + Literal(current) + " -Target " + Literal(target) + " | Out-Null";
        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var result = await CommandRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]);
        if (!result.Succeeded) throw new IOException(result.Output);
    }

    public static async Task<GithubRelease> GetLatestAsync(string url)
    {
        string path = RepositoryPath(url);
        using var response = await Http.GetAsync("https://api.github.com/repos/" + path + "/releases/latest");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("GitHub release request failed: " + (int)response.StatusCode + ". Check the URL, published releases and GitHub rate limit.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        List<GithubAsset> assets = [];
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) continue;
            string? digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            assets.Add(new(name, a.GetProperty("browser_download_url").GetString()!, digest));
        }
        if (assets.Count == 0) throw new InvalidOperationException("Latest release has no ZIP/7z assets. Use Open GitHub releases to install manually.");
        return new(doc.RootElement.GetProperty("tag_name").GetString() ?? "", assets);
    }
    public static async Task<string> UninstallAsync(string repositoryUrl, string installedPath)
    {
        string repository = RepositoryPath(repositoryUrl);
        string logical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installedPath));
        var directory = new DirectoryInfo(logical);
        if (!directory.Exists) throw new DirectoryNotFoundException(logical);
        bool linked = directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
        string physical = linked ? directory.ResolveLinkTarget(true)?.FullName
            ?? throw new IOException("Cannot resolve current link.") : logical;
        physical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physical));
        string marker = Path.Combine(physical, ".wam-installed.json");
        if (!File.Exists(marker) || File.Exists(ScoopMetadata.ReceiptPath(physical)))
            throw new IOException("This folder is not a recognized App Manager GitHub installation, or belongs to Scoop.");
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(marker)))
        {
            string url = doc.RootElement.GetProperty("Url").GetString() ?? "";
            if (!url.StartsWith("https://github.com/" + repository + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The installed release does not match this repository. No files changed.");
        }
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                string? exe = null;
                try { exe = process.MainModule?.FileName; } catch { }
                if (exe != null && (exe.StartsWith(physical + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || exe.StartsWith(logical + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Close the running app before uninstalling: " + process.ProcessName);
            }
        }
        string parent = Path.GetDirectoryName(physical) ?? throw new IOException("Cannot remove a drive root.");
        string archiveRoot = Path.Combine(parent, ".wam-removed");
        Directory.CreateDirectory(archiveRoot);
        string destination = Path.Combine(archiveRoot, Path.GetFileName(physical) + "-" + Guid.NewGuid().ToString("N"));
        // Same-volume move retains config/data; never recursively delete a junction target.
        Directory.Move(physical, destination);
        if (linked)
        {
            try { Directory.Delete(logical, false); }
            catch
            {
                Directory.Move(destination, physical);
                throw;
            }
        }
        return destination;
    }

    public static async Task InstallAsync(GithubAsset asset, string target, string scoopRoot)
    {
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Destination already exists. Choose a new folder.");
        string temp = Path.Combine(Path.GetTempPath(), "wam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool created = false;
        try
        {
            string archive = Path.Combine(temp, "release" + Path.GetExtension(asset.Name));
            string? sevenZip = null;
            if (archive.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                sevenZip = new[] { Path.Combine(scoopRoot, "apps", "7zip", "current", "7z.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe") }.FirstOrDefault(File.Exists);
                if (sevenZip == null) throw new FileNotFoundException("This release uses 7z. Install 7-Zip through Scoop or the standard installer first.");
            }
            using (var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = new FileStream(archive, FileMode.CreateNew);
                await response.Content.CopyToAsync(stream);
            }
            if (asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            {
                await using var stream = File.OpenRead(archive);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!hash.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("GitHub asset checksum mismatch.");
            }
            Directory.CreateDirectory(target); created = true;
            if (sevenZip == null) await Task.Run(() => ZipFile.ExtractToDirectory(archive, target));
            else
            {
                var result = await CommandRunner.RunAsync(sevenZip, ["x", archive, "-o" + target, "-y"]);
                if (!result.Succeeded) throw new IOException("7-Zip extraction failed: " + result.Output);
            }
            if (!Directory.EnumerateFiles(target, "*.exe", SearchOption.AllDirectories).Any())
                throw new IOException("Archive contains no Windows executable. Select a Windows binary release.");
            await File.WriteAllTextAsync(Path.Combine(target, ".wam-installed.json"), JsonSerializer.Serialize(new { asset.Name, asset.Url, Installed = DateTimeOffset.Now }));
        }
        catch
        {
            if (created) { try { Directory.Delete(target, true); } catch { } }
            throw;
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }
}
