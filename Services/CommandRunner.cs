using System.IO;
using System.Diagnostics;
using System.Text;

namespace WindowsAppManager.Services;

public record CommandResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken token = default)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        // Drain stdout/stderr simultaneously to avoid deadlocks on large CLI output.
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        Task<string> errors = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        string text = (await output).TrimEnd() + "\n" + (await errors).TrimEnd();
        return new CommandResult(process.ExitCode, text.Trim());
    }

    public static Task<CommandResult> WinGetAsync(params string[] args)
        => RunAsync("winget.exe", args);

    public static Task<CommandResult> ScoopAsync(string scoopRoot, params string[] args)
    {
        string script = Path.Combine(scoopRoot, "apps", "scoop", "current", "bin", "scoop.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Không tìm thấy Scoop. Kiểm tra Scoop root.", script);
        var shellArgs = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script };
        shellArgs.AddRange(args);
        return RunAsync("powershell.exe", shellArgs);
    }
}
