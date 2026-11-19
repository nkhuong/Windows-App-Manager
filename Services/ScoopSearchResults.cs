using System.Text.RegularExpressions;

namespace WindowsAppManager.Services;

internal sealed record ScoopSearchResult(string Name, string Version, string Bucket, bool RequiresBucket);

internal static class ScoopSearchResults
{
    public static List<ScoopSearchResult> Parse(string output)
    {
        List<ScoopSearchResult> results = [];
        // Scoop's local table has a Version column. Its remote table only has Name and Source.
        MatchCollection tables = Regex.Matches(output,
            @"(?im)^[ \t]*Name[ \t]+(?:(?<version>Version)[ \t]+)?Source(?:[ \t]+Binaries)?[ \t]*\r?\n[ \t]*-+(?:[ \t]+-+)+[ \t]*\r?$");
        foreach (Match table in tables)
        {
            bool local = table.Groups["version"].Success;
            foreach (string raw in output[(table.Index + table.Length)..].TrimStart('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                var row = Regex.Match(line, local
                    ? @"^(?<name>[\w.+-]+)\s+(?<version>\S+)\s+(?<bucket>[\w.-]+)(?:\s+.*)?$"
                    : @"^(?<name>[\w.+-]+)\s+(?<bucket>[\w.-]+)$");
                if (!row.Success) break;
                results.Add(new(row.Groups["name"].Value, row.Groups["version"].Value,
                    row.Groups["bucket"].Value, !local));
            }
        }
        return results.DistinctBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
