using System.Windows;
using System.Text.RegularExpressions;
using WindowsAppManager.Services;
using WindowsAppManager.Models;
namespace WindowsAppManager.Dialogs;

public sealed record PackageResult(string Name, string PackageId, string Version, string Source);

public partial class SearchDialog : Window
{
    private readonly string scoopRoot;
    private bool searching;
    public event Action<PlannedApp>? PlannedAdded;
    public SearchDialog(string root) { InitializeComponent(); scoopRoot = root; }
    private async void SearchWingetClick(object sender, RoutedEventArgs e) => await SearchAsync("WinGet");
    private async void SearchScoopClick(object sender, RoutedEventArgs e) => await SearchAsync("Scoop");
    private async Task SearchAsync(string source)
    {
        if (searching) return;
        string query = QueryBox.Text.Trim();
        if (query.Length == 0) { MessageBox.Show(this, "Enter a search query."); return; }
        searching = true;
        ResultsGrid.ItemsSource = null;
        ResultsBox.Text = $"Searching {source} for {query}...";
        try
        {
            var result = source == "WinGet"
                ? await CommandRunner.WinGetAsync("search", query, "--accept-source-agreements", "--disable-interactivity")
                : await CommandRunner.ScoopAsync(scoopRoot, "search", query);
            ResultsBox.Text = "COMMAND (PowerShell syntax):\n" + result.CommandLine + "\n\n" + result.Output;
            List<PackageResult> rows = result.Succeeded ? ParseResults(result.Output, source) : [];
            ResultsGrid.ItemsSource = rows;
            Hint.Text = $"{rows.Count} results. Select a row and click Add to plan. Raw output is below.";
        }
        catch (Exception ex) { ResultsBox.Text = ex.Message; }
        finally { searching = false; }
    }

    private static List<PackageResult> ParseResults(string output, string source)
    {
        var rows = new List<PackageResult>();
        string bucket = "";
        bool table = false;
        foreach (string raw in Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "").Split('\n'))
        {
            string line = raw.Trim();
            var bucketMatch = Regex.Match(line, @"^'([^']+)' bucket:");
            if (bucketMatch.Success) { bucket = bucketMatch.Groups[1].Value; table = false; continue; }
            if (Regex.IsMatch(line, @"^-{3,}(?:\s+-+)*$")) { table = true; continue; }
            if (!table || line.Length == 0) continue;
            var columns = Regex.Split(line, @"\s{2,}");
            if (source == "WinGet")
            {
                if (columns.Length >= 3 && Regex.IsMatch(columns[1], @"^[A-Za-z0-9][A-Za-z0-9._+-]*$")
                    && !columns[1].Contains('…'))
                    rows.Add(new(columns[0], columns[1], columns[2], source));
            }
            else
            {
                // Scoop uses PowerShell table output with bucket/source as the third column.
                var parts = Regex.Split(line, @"\s+");
                if (parts.Length >= 2 && Regex.IsMatch(parts[0], @"^[a-zA-Z0-9][a-zA-Z0-9._+-]*$"))
                {
                    string b = parts.Length >= 3 ? parts[2] : bucket;
                    string id = parts[0];
                    rows.Add(new(parts[0], id, parts[1], source));
                }
            }
        }
        return rows.DistinctBy(x => x.PackageId).ToList();
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (searching || ResultsGrid.SelectedItem is not PackageResult row)
        {
            MessageBox.Show(this, "Select a package row first. If no rows appear, check the raw output.");
            return;
        }
        PlannedAdded?.Invoke(new PlannedApp { Name = row.Name, PackageId = row.PackageId, ManagedBy = row.Source });
        Hint.Text = $"Added {row.Name} ({row.PackageId}) to your saved plan.";
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
