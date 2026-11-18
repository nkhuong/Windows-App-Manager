using System.IO;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WindowsAppManager.Dialogs;
using WindowsAppManager.Models;
using WindowsAppManager.Services;

namespace WindowsAppManager;

public partial class MainWindow : Window
{
    private readonly AppStore store = new();
    private readonly InventoryScanner inventory;
    private BackupService backups;
    private List<AppItem> allItems = [];
    private AppItem? selected;
    private bool busy;
    private bool initialLoadComplete;
    private bool updatesKnown;
    private string updateDetails = "Not checked yet.";
    public ObservableCollection<AppItem> DisplayItems { get; } = [];

    public MainWindow()
    {
        inventory = new InventoryScanner(store);
        backups = new BackupService(store, inventory.ScoopRoot);
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) =>
        {
            initialLoadComplete = true;
            await RefreshAsync();
        };
    }

    private void SetBusy(bool value, string status)
    {
        busy = value;
        BusyBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
        UpdateButtons();
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        updatesKnown = false;
        UpdatesCount.Text = "…";
        SetBusy(true, "Scanning Windows Registry, Scoop and WinGet... This may take a moment.");
        try
        {
            var result = await inventory.ScanAsync();
            allItems = result.Apps;
            ApplyFilter();
            var updateWarnings = await UpdateChecker.CheckAsync(allItems, inventory.ScoopRoot);
            updatesKnown = updateWarnings.Count == 0 && result.Warnings.Count == 0;
            updateDetails = UpdateChecker.LastDetails + "\n\n" + string.Join("\n", result.Warnings.Concat(updateWarnings));
            ApplyFilter();
            result.Warnings.AddRange(updateWarnings);
            StatusText.Text = $"Loaded {allItems.Count} entries. Scoop: {inventory.ScoopRoot}" +
                (result.Warnings.Count > 0 ? " | " + string.Join("; ", result.Warnings) : "");
        }
        catch (Exception ex)
        {
            updateDetails = "Scan failed: " + ex.Message;
            StatusText.Text = updateDetails;
            MessageBox.Show(this, ex.Message, "Inventory error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { busy = false; BusyBar.Visibility = Visibility.Collapsed; UpdateButtons(); }
    }

    private void ApplyFilter()
    {
        if (!initialLoadComplete || SearchBox == null || SourceFilter == null || StatusFilter == null) return;
        string query = SearchBox.Text.Trim();
        string source = (SourceFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All sources";
        string status = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All apps";
        var filtered = allItems.Where(a =>
            (query.Length == 0 || (a.Name + " " + a.PackageId + " " + a.InstallPath)
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            && (source == "All sources" || a.ManagedBy == source)
            && (status switch
            {
                "Installed" => a.IsInstalled,
                "Not installed" => !a.IsInstalled,
                "Updates" => a.UpdateAvailable,
                "Planned" => a.IsPlanned && !a.IsInstalled,
                _ => true
            })).ToList();
        void MarkFilter(Button button, bool active)
        {
            button.Background = (System.Windows.Media.Brush)FindResource(active ? "Accent" : "Panel");
        }
        MarkFilter(InstalledFilterButton, source == "All sources" && status == "Installed");
        MarkFilter(PlannedFilterButton, source == "All sources" && status == "Planned");
        MarkFilter(UpdatesFilterButton, source == "All sources" && status == "Updates");
        MarkFilter(ScoopFilterButton, source == "Scoop" && status == "All apps");
        string? selectedId = selected?.Id;
        DisplayItems.Clear();
        foreach (var app in filtered) DisplayItems.Add(app);
        InstalledCount.Text = allItems.Count(x => x.IsInstalled).ToString();
        PlannedCount.Text = allItems.Count(x => x.IsPlanned && !x.IsInstalled).ToString();
        UpdatesCount.Text = updatesKnown ? allItems.Count(x => x.IsInstalled && x.UpdateAvailable).ToString() : allItems.Count(x => x.IsInstalled && x.UpdateAvailable).ToString() + "+";
        if (selectedId != null)
        {
            AppItem? retained = filtered.FirstOrDefault(x => x.Id == selectedId);
            AppsGrid.SelectedItem = retained;
            if (retained == null) { selected = null; DetailsPanel.DataContext = null; }
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (InstallButton == null) return;
        if (InstallScoopButton != null)
        {
            InstallScoopButton.Visibility = ScoopSetup.IsInstalled(inventory.ScoopRoot) ? Visibility.Collapsed : Visibility.Visible;
            InstallScoopButton.IsEnabled = !busy;
        }
        bool has = selected != null;
        ReleasesButton.Visibility = selected?.ManagedBy == "GitHub" ? Visibility.Visible : Visibility.Collapsed;
        ReleasesButton.IsEnabled = !busy;
        bool managed = has && (selected!.ManagedBy == "WinGet" || selected.ManagedBy == "Scoop")
            && !string.IsNullOrWhiteSpace(selected.PackageId);
        InstallButton.IsEnabled = !busy && has && !selected!.IsInstalled && (managed || selected.ManagedBy == "GitHub");
        UninstallButton.IsEnabled = !busy && has && selected!.IsInstalled && (managed ||
            (selected.ManagedBy == "GitHub" && store.State.GithubInstallations.ContainsKey(selected.PackageId)));
        UpgradeButton.IsEnabled = !busy && managed && selected!.IsInstalled;
        BackupButton.IsEnabled = !busy && has && selected!.IsInstalled;
        OpenFolderButton.IsEnabled = !busy && has &&
            (Directory.Exists(selected!.InstallPath) || File.Exists(selected.InstallPath));
        RemovePlanButton.IsEnabled = !busy && has && selected!.IsPlanned;
        EditPlanButton.IsEnabled = !busy && has && selected!.IsPlanned && !selected.IsInstalled;
    }

    private async void InstallScoopClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (ScoopSetup.IsElevated())
        {
            MessageBox.Show(this, "Close App Manager and reopen it without Run as administrator to install Scoop for your user.", "Install Scoop");
            return;
        }
        var dialog = new ScoopSetupDialog(inventory.ScoopRoot) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string root = dialog.InstallRoot;
        SetBusy(true, "Installing Scoop...");
        var log = new OutputDialog("Install Scoop", "Downloading the official Scoop installer...\n") { Owner = this };
        log.BeginRunning();
        log.Show();
        var progress = new Progress<string>(log.AppendOutput);
        try
        {
            var result = await ScoopSetup.InstallAsync(root, progress);
            if (!result.Succeeded) throw new IOException("Scoop installer exited with code " + result.ExitCode + ". See log above.");
            if (!ScoopSetup.IsInstalled(root)) throw new IOException("Installer finished, but scoop.ps1 was not found. See log above.");
            var check = await CommandRunner.ScoopStreamingAsync(root, ["--version"], progress);
            if (!check.Succeeded) throw new IOException("Scoop was found but its version check failed. See log above.");
            store.State.ScoopRoot = root;
            store.Save();
            Environment.SetEnvironmentVariable("SCOOP", root);
            inventory.SetScoopRoot(root);
            backups = new BackupService(store, root);
            log.AppendOutput("\nScoop installed. You can search and install Scoop apps now.\n");
            log.Complete("Scoop installation completed");
        }
        catch (Exception ex)
        {
            log.AppendOutput("\nERROR: " + ex.Message + "\n");
            log.Complete("Scoop installation failed");
        }
        finally { SetBusy(false, "Ready"); }
        await RefreshAsync();
    }

    private void SelectStatus(string status)
    {
        bool active = SourceFilter.SelectedIndex == 0
            && (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() == status;
        SourceFilter.SelectedIndex = 0;
        if (active) { StatusFilter.SelectedIndex = 0; ApplyFilter(); return; }
        foreach (ComboBoxItem item in StatusFilter.Items)
        {
            if (item.Content?.ToString() == status)
            {
                StatusFilter.SelectedItem = item;
                ApplyFilter();
                break;
            }
        }
    }

    private void ShowInstalledClick(object sender, RoutedEventArgs e) => SelectStatus("Installed");
    private void ShowPlannedClick(object sender, RoutedEventArgs e) => SelectStatus("Planned");
    private void ShowUpdatesClick(object sender, RoutedEventArgs e) => SelectStatus("Updates");

    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void FilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selected = AppsGrid.SelectedItem as AppItem;
        if (DetailsPanel != null) DetailsPanel.DataContext = selected;
        UpdateButtons();
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            store.AddPlanned(dialog.Result);
            _ = RefreshAsync();
        }
    }
    private void SearchNewClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SearchDialog(inventory.ScoopRoot) { Owner = this };
        dialog.PlannedAdded += a => store.AddPlanned(a);
        dialog.ShowDialog();
        _ = RefreshAsync();
    }
    private void RemovePlanClick(object sender, RoutedEventArgs e)
    {
        if (selected is not { IsPlanned: true } app) return;
        if (MessageBox.Show(this, $"Remove {app.Name} from your planned app list?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        store.RemovePlanned(new PlannedApp { ManagedBy = app.ManagedBy, PackageId = app.PackageId, InstallPath =
            app.ManagedBy == "Manual" ? app.InstallPath : "" });
        _ = RefreshAsync();
    }

    private async void UpdatesClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        await RefreshAsync();
        new OutputDialog("Update check results", updateDetails) { Owner = this }.ShowDialog();
    }

    private void ClearSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void ShowScoopClick(object sender, RoutedEventArgs e)
    {
        bool active = SourceFilter.SelectedIndex == 2 && StatusFilter.SelectedIndex == 0;
        SourceFilter.SelectedIndex = active ? 0 : 2;
        StatusFilter.SelectedIndex = 0;
        ApplyFilter();
    }

    private async void EditPlanClick(object sender, RoutedEventArgs e)
    {
        if (busy || selected is not { IsPlanned: true, IsInstalled: false } app) return;
        var dialog = new AddAppDialog(app.Name, app.PackageId, app.ManagedBy) { Owner = this, Title = "Edit planned app" };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;
        store.ReplacePlanned(new PlannedApp { Name = app.Name, ManagedBy = app.ManagedBy,
            PackageId = app.PackageId, InstallPath = app.ManagedBy == "Manual" ? app.InstallPath : "" }, dialog.Result);
        await RefreshAsync();
    }

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (selected?.ManagedBy == "GitHub") await InstallGithubAsync();
        else await ExecuteActionAsync("install");
    }

    private async Task InstallGithubAsync()
    {
        if (busy || selected is not { ManagedBy: "GitHub", IsInstalled: false } app) return;
        SetBusy(true, "Loading GitHub release assets...");
        OutputDialog? log = null;
        try
        {
            var release = await GithubInstaller.GetLatestAsync(app.PackageId);
            var pick = new GithubAssetDialog(release.Assets) { Owner = this };
            if (pick.ShowDialog() != true || pick.SelectedAsset == null) return;
            var folder = new OpenFolderDialog { Title = "Choose parent folder for portable GitHub apps" };
            if (Directory.Exists(@"D:\PortableApps")) folder.InitialDirectory = @"D:\PortableApps";
            if (folder.ShowDialog(this) != true) return;
            string root = Path.Combine(folder.FolderName, GithubInstaller.RepositoryName(app.PackageId));
            string target = Path.Combine(root, GithubInstaller.VersionFolder(release.Version, pick.SelectedAsset.Name));
            string current = Path.Combine(root, "current");
            if (Directory.Exists(current) || File.Exists(current))
                throw new IOException("The current path already exists. Choose another parent folder; existing installations are preserved.");
            if (MessageBox.Show(this, $"Download and extract {pick.SelectedAsset.Name}?\n\nRepository: {app.PackageId}\nVersion: {release.Version}\nDestination: {target}",
                "Install portable GitHub release", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            StatusText.Text = "Downloading and extracting " + pick.SelectedAsset.Name + "...";
            log = new OutputDialog("Install: " + app.Name, "Starting GitHub install...\n") { Owner = this };
            log.BeginRunning();
            log.Show();
            var progress = new Progress<string>(log.AppendOutput);
            await GithubInstaller.InstallAsync(pick.SelectedAsset, target, inventory.ScoopRoot, progress);
            string installedPath = target;
            string linkNote = "";
            try { await GithubInstaller.CreateCurrentAsync(current, target); installedPath = current; }
            catch (Exception ex) { linkNote = "\nThe release was installed but current could not be created: " + ex.Message; }
            store.State.GithubInstallations[app.PackageId] = new GithubInstallation
                { Path = installedPath, Version = release.Version, RootPath = root, VersionPath = target };
            store.Save();
            log.Complete("GitHub installation completed");
            MessageBox.Show(this, "Installed portable release to:\n" + target + "\nLaunch path: " + installedPath + linkNote + "\n\nUse Open folder to launch the app. Required runtimes are installed separately.", "GitHub install completed");
        }
        catch (Exception ex)
        {
            if (log != null) { log.AppendOutput("\nERROR: " + ex.Message + "\n"); log.Complete("GitHub installation failed"); }
            else MessageBox.Show(this, ex.Message, "GitHub install failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false, "Ready"); }
        await RefreshAsync();
    }
    private async void UninstallClick(object sender, RoutedEventArgs e)
    {
        if (selected?.ManagedBy != "GitHub") { await ExecuteActionAsync("uninstall"); return; }
        if (busy || selected is not { IsInstalled: true } app
            || !store.State.GithubInstallations.TryGetValue(app.PackageId, out var installation)) return;
        if (MessageBox.Show(this, $"Uninstall portable {app.Name}?\n\nClose the app first.\nPath: {installation.Path}\n\nThe active version and its data will be moved to a .wam-removed folder beside the version folder. Other versions are kept. AppData is left in place.",
            "Confirm GitHub uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetBusy(true, "Removing portable " + app.Name + "...");
        var log = new OutputDialog("Uninstall: " + app.Name, "Starting GitHub uninstall...\n") { Owner = this };
        log.BeginRunning();
        log.Show();
        var progress = new Progress<string>(log.AppendOutput);
        try
        {
            string removedPath = await GithubInstaller.UninstallAsync(app.PackageId, installation.Path, progress);
            store.AddPlanned(new PlannedApp { Name = app.Name, ManagedBy = "GitHub", PackageId = app.PackageId });
            store.State.GithubInstallations.Remove(app.PackageId);
            store.Save();
            log.Complete("GitHub uninstall completed");
            MessageBox.Show(this, "Removed from installed apps and returned to Planned.\nFiles were kept at:\n" + removedPath,
                "GitHub uninstall completed");
        }
        catch (Exception ex)
        {
            log.AppendOutput("\nERROR: " + ex.Message + "\n");
            log.Complete("GitHub uninstall failed");
        }
        finally { SetBusy(false, "Ready"); }
        await RefreshAsync();
    }
    private async void UpgradeClick(object sender, RoutedEventArgs e) => await ExecuteActionAsync("upgrade");

    private async Task ExecuteActionAsync(string action)
    {
        if (busy || selected == null) return;
        var app = selected;
        string source = app.ManagedBy;
        if (source is not ("WinGet" or "Scoop") || app.PackageId.Length == 0) return;
        string verb = action.ToUpperInvariant();
        string msg = $"{verb} {app.Name}?\n\nSource: {source}\nPackage: {app.PackageId}\nPath: {app.PathText}\n\n";
        if (action == "uninstall") msg += "WARNING: Uninstall may remove settings. This action is not automatically backed up.";
        if (source == "WinGet") msg += "\n\nWinGet works by package ID; Windows may have more than one install. Verify the target before proceeding.";
        if (MessageBox.Show(this, msg, "Confirm " + verb,
            MessageBoxButton.YesNo, action == "uninstall" ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool installReportedSuccess = false;
        SetBusy(true, $"{action} {app.Name} via {source}...");
        var log = new OutputDialog($"{verb}: {app.Name}", "Starting...\n");
        log.Owner = this;
        log.BeginRunning();
        log.Show();
        var progress = new Progress<string>(log.AppendOutput);
        try
        {
            CommandResult result;
            if (source == "WinGet")
            {
                var arguments = new List<string> { action, "--id", app.PackageId, "--exact" };
                if (action is "install" or "upgrade")
                    arguments.AddRange(["--accept-package-agreements", "--accept-source-agreements"]);
                result = await CommandRunner.RunAsync("winget.exe", arguments, output: progress);
            }
            else
            {
                result = await CommandRunner.ScoopStreamingAsync(inventory.ScoopRoot,
                    [action == "upgrade" ? "update" : action, app.PackageId], progress);
            }
            log.Complete($"{verb}: {app.Name} (exit {result.ExitCode})");
            if (result.Succeeded && action == "install")
            {
                installReportedSuccess = true;
                store.RecordInstalled(source, app.PackageId);
            }
            if (result.Succeeded && action == "uninstall")
                store.AddPlanned(new PlannedApp { Name = app.Name, ManagedBy = source, PackageId = app.PackageId });
            StatusText.Text = result.Succeeded ? $"{action} finished. Refreshing..." : $"{action} returned error {result.ExitCode}.";
        }
        catch (Exception ex)
        {
            log.AppendOutput("\nERROR: " + ex.Message + "\n");
            log.Complete($"{verb}: {app.Name} — failed");
        }
        finally { busy = false; BusyBar.Visibility = Visibility.Collapsed; UpdateButtons(); }
        await RefreshAsync();
        if (installReportedSuccess && !allItems.Any(a => a.IsInstalled && a.ManagedBy == source
            && a.PackageId.Equals(app.PackageId, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "Install command succeeded, but the app was not detected after scanning: " + app.PackageId;
            MessageBox.Show(this, "The installer returned success, but inventory could not confirm the installation.\n\nPackage: "
                + app.PackageId + "\nScoop root: " + inventory.ScoopRoot
                + "\n\nCheck the command output and installation folder before trying again.",
                "Installation not confirmed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BackupClick(object sender, RoutedEventArgs e)
    {
        if (busy || selected is not { IsInstalled: true } app) return;
        var profile = backups.GetProfile(app);
        IReadOnlyList<string>? manuallySelected = null;
        if (profile == null)
        {
            MessageBox.Show(this, "No automatic backup profile found for this app. Select specific config/data files to back up.\n\nA backup profile can be created later for automatic backups.", "Manual backup", MessageBoxButton.OK, MessageBoxImage.Information);
            var picker = new OpenFileDialog { Title = "Choose config/data files to back up", Multiselect = true,
                Filter = "All files|*.*" };
            if (picker.ShowDialog(this) != true) return;
            manuallySelected = picker.FileNames;
        }
        if (MessageBox.Show(this, $"Back up data for {app.Name}?\n\nClose the app first, especially if its database is open.\nDestination: {store.BackupRoot}",
            "Backup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetBusy(true, "Backing up " + app.Name + "...");
        try
        {
            var result = await backups.BackupAsync(app, manuallySelected);
            MessageBox.Show(this, $"Saved {result.Files} file(s) to:\n{result.Path}\n\nbackup-info.json records the original source paths.",
                "Backup completed", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Backup saved: " + result.Path;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message + "\n\nThe backup was not completed.", "Backup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "Backup failed. No source files were changed.";
        }
        finally { busy = false; BusyBar.Visibility = Visibility.Collapsed; UpdateButtons(); }
    }

    private void ReleasesClick(object sender, RoutedEventArgs e)
    {
        if (selected?.ManagedBy != "GitHub") return;
        if (Uri.TryCreate(selected.PackageId, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Trim('/').Split('/').Length == 2)
            Process.Start(new ProcessStartInfo("https://github.com/" + uri.AbsolutePath.Trim('/') + "/releases") { UseShellExecute = true });
    }

    private void OpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        string path = selected.InstallPath;
        if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? "";
        if (!Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true,
            ArgumentList = { path } });
    }
}
