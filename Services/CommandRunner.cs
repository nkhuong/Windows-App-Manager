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
    public static async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> args,
        CancellationToken token = default, IProgress<string>? output = null,
        IReadOnlyDictionary<string, string>? environment = null)
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
        if (environment != null)
            foreach (var pair in environment) process.StartInfo.Environment[pair.Key] = pair.Value;
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var captured = new StringBuilder();
        object sync = new();
        async Task PumpAsync(StreamReader reader)
        {
            char[] buffer = new char[2048];
            int count;
            // Read chunks instead of lines: progress text may use carriage returns without newlines.
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                string chunk = new(buffer, 0, count);
                lock (sync)
                {
                    captured.Append(chunk);
                    output?.Report(chunk);
                }
            }
        }
        Task stdout = PumpAsync(process.StandardOutput);
        Task stderr = PumpAsync(process.StandardError);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(token), stdout, stderr);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return new CommandResult(process.ExitCode, captured.ToString().Trim());
    }

    public static Task<CommandResult> WinGetAsync(params string[] args)
        => RunAsync("winget.exe", args);

    public static Task<CommandResult> ScoopAsync(string scoopRoot, params string[] args)
        => ScoopStreamingAsync(scoopRoot, args);

    public static Task<CommandResult> ScoopStreamingAsync(string scoopRoot, IEnumerable<string> args,
        IProgress<string>? output = null)
    {
        string script = Path.Combine(scoopRoot, "apps", "scoop", "current", "bin", "scoop.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Không tìm thấy Scoop. Nhấn Install Scoop trên cửa sổ chính.", script);
        var shellArgs = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script };
        shellArgs.AddRange(args);
        return RunAsync("powershell.exe", shellArgs, output: output, environment: new Dictionary<string, string>
        {
            ["SCOOP"] = scoopRoot,
            ["PATH"] = Path.Combine(scoopRoot, "shims") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        });
    }
}
