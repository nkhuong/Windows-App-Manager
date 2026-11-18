namespace WindowsAppManager.Services;

public static class ScoopMetadata
{
    // Scoop 0.6+ prefixes these files to avoid collisions with app-owned JSON.
    public static string ReceiptPath(string directory) => PreferPrefixed(directory, "scoop-install.json", "install.json");
    public static string ManifestPath(string directory) => PreferPrefixed(directory, "scoop-manifest.json", "manifest.json");

    private static string PreferPrefixed(string directory, string currentName, string legacyName)
    {
        string current = Path.Combine(directory, currentName);
        return File.Exists(current) ? current : Path.Combine(directory, legacyName);
    }
}
