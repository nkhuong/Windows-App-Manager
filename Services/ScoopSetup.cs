using System.Net.Http;
using System.Security.Principal;

namespace WindowsAppManager.Services;

public static class ScoopSetup
{
    public static bool IsInstalled(string root) => File.Exists(Path.Combine(root, "apps", "scoop", "current", "bin", "scoop.ps1"));

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task<CommandResult> InstallAsync(string root, IProgress<string> output)
    {
        if (IsElevated()) throw new InvalidOperationException("Reopen App Manager without administrator privileges to install Scoop.");
        root = Path.GetFullPath(root);
        if (IsInstalled(root)) throw new IOException("Scoop is already installed in this folder. Refresh App Manager.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("Choose an empty folder. Existing files will not be overwritten.");
        string temporary = Path.Combine(Path.GetTempPath(), "wam-scoop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            string installer = await client.GetStringAsync("https://get.scoop.sh");
            string installerPath = Path.Combine(temporary, "install.ps1");
            await File.WriteAllTextAsync(installerPath, installer);
            string wrapperPath = Path.Combine(temporary, "setup.ps1");
            await File.WriteAllTextAsync(wrapperPath, """
                param([string]$InstallerPath, [string]$ScoopDir)
                $ErrorActionPreference = 'Stop'
                [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
                try {
                    Write-Output ('PowerShell language mode: ' + $ExecutionContext.SessionState.LanguageMode)
                    Write-Output ('Effective execution policy: ' + (Get-ExecutionPolicy))
                    Get-ExecutionPolicy -List | Format-Table -AutoSize | Out-String | Write-Output
                    if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
                        throw 'Scoop requires FullLanguage mode. This PowerShell session is restricted by system policy.'
                    }
                    Write-Output 'Starting the official Scoop installer...'
                    & $InstallerPath -ScoopDir $ScoopDir
                    if ($LASTEXITCODE) { exit $LASTEXITCODE }
                } catch {
                    [Console]::Error.WriteLine(($_ | Format-List * -Force | Out-String))
                    [Console]::Error.WriteLine($_.Exception.ToString())
                    [Console]::Error.WriteLine($_.InvocationInfo.PositionMessage)
                    [Console]::Error.WriteLine('Scoop installation stopped. No attempt was made to change user or organization execution policies.')
                    exit 1
                }
                """);
            output.Report("Installing Scoop to " + root + "\n");
            return await CommandRunner.RunAsync("powershell.exe",
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", wrapperPath,
                 "-InstallerPath", installerPath, "-ScoopDir", root], output: output);
        }
        finally { try { Directory.Delete(temporary, recursive: true); } catch { /* Temporary files only. */ } }
    }
}
