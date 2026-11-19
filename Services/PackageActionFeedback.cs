using System.Text.RegularExpressions;

namespace WindowsAppManager.Services;

internal static class PackageActionFeedback
{
    internal const int WinGetUpdateNotApplicable = unchecked((int)0x8A15002B);

    public static string? ScoopFailure(CommandResult result)
    {
        string output = Regex.Replace(result.Output, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        if (output.Contains("Couldn't find manifest", StringComparison.OrdinalIgnoreCase))
            return "Scoop could not find the package manifest. Check the package ID and add its bucket if it is missing.";

        bool downloadFailed = Regex.IsMatch(output, @"(?im)^\s*ERROR URL\b");
        if (downloadFailed && Regex.IsMatch(output,
            @"connection attempt failed|connected host has failed to respond|timed out|unable to connect|could not connect",
            RegexOptions.IgnoreCase))
            return "Scoop could not connect to the download server. Check the package URL and network connection, then retry.";

        if (downloadFailed)
            return "Scoop could not download the package. See the URL and error in the log.";

        return result.Succeeded ? null : $"Scoop exited with code {result.ExitCode}. See the log for details.";
    }
}
