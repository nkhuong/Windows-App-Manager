# Windows App Manager — runnable WPF MVP (v0.1)

A small, **real WPF Windows desktop project** for a Windows 11 PC with WinGet and Scoop.
Built in C# / .NET **11** (the SDK the user already has), no NuGet dependencies.

## Run in Visual Studio 2026

1. **Back up the existing empty project** at `D:\sites\WindowsAppManager` before overwriting any files.
2. Extract the entire ZIP into `D:\sites\WindowsAppManager\`, replacing the empty WPF template files.
3. Open `WindowsAppManager.csproj` in Visual Studio 2026.
4. Check that the WPF/.NET desktop development workload and .NET 11 SDK are installed (`dotnet --list-sdks`).
5. Press **F5**. The first inventory scan can take a while depending on WinGet.

Alternative command line:

```powershell
cd D:\sites\WindowsAppManager
dotnet run
```

If your Visual Studio project has a different framework selected, use the project's `TargetFramework` from this ZIP (`net11.0-windows`) or install .NET 10 SDK and switch the target to `net10.0-windows`.

## Implemented in this version

- Read-only Windows uninstall registry inventory (HKLM/HKCU, both registry views).
- Scoop installed apps scan, with a *stable* `current` path and approximate folder size (fast, capped scan).
- WinGet recognition using `winget list` and conservative matching; WinGet entries that are not in the traditional Windows uninstall registry are also shown.
- Separate `InstalledVia` and `ManagedBy`; different installation paths are listed separately.
- Search/filter by name, package ID and path; source and status filters.
- Add and remove Planned apps (local JSON); save apps for the next Windows installation.
- Search package catalog through WinGet and Scoop (raw command output, then add the exact package ID to Planned).
- Install, upgrade, uninstall via selected package manager, with confirmation and command output.
- Check for updates with `winget upgrade` and `scoop status`.
- **Backup files** button. Profile-based automatic backup for Video Duplicate Finder; for others choose files manually.
- All backup metadata recorded in `backup-info.json` to support a Restore feature later.
- Does **not** silently delete app settings/data during uninstall.

## Backup profiles and state

Data lives on a separate disk when available:

```text
D:\AppConfig\WindowsAppManager\
  state.json                   planned entries and install history
  Backups\                     timestamped backups (including backup-info.json)
  BackupProfiles\              user-created or overridden backup profiles
```

A sample VDF profile is included at `BackupProfiles\videoduplicatefinder.json`. It searches for:

- `ScannedFiles.db`
- `Settings.json`
- `backup.scanresults` (optional)
- `DenseEmbeddings.db` (optional)

**Close VDF/Jellyfin/other applications before backing up a database.** A running database copy can be inconsistent.
The profile checks Scoop's `persist` first, then the active install folder; it never deletes a source file.
If you install VDF manually and its installation name differs, adapt `appNames` / `packageIds` in the profile.

For a custom profile, copy a JSON file into the user `BackupProfiles` folder and include the full paths using `{InstallPath}`, `{ScoopPersist}`, `{AppData}`, or `{LocalAppData}`.

## Important limitations in v0.1

- **This is a functional MVP, not a mature package manager.** Test with a harmless app before using destructive actions.
- WinGet's human-readable table is not a guaranteed stable API; localized/changed output might not match. Non-unique matches are left unlinked instead of guessed. A next version should use the structured `Microsoft.WinGet.Client` API.
- The original tool used for an existing Registry app is unknowable in many cases; `InstalledVia` correctly starts as `Unknown`. When the app itself installs a package, it records the source.
- If one product is installed twice, *different paths are not merged*. A single WinGet package ID cannot always target a particular copy; review uninstall confirmation carefully. A future version should resolve the precise uninstall entry.
- The Installed size shown by Registry is estimated; Scoop scanning is capped at 5,000 files per app and displays `≥` for partial sizes. Install dates can be absent or reflect updates.
- Manual/GitHub entries are tracked as Planned but direct install/upgrade/uninstall is not supported yet. For these, define an explicit installation recipe/manifest in a future version. The app never downloads an arbitrary URL or executes an untrusted script on its own.
- There is **Backup**, but not yet **Restore** (no accidental overwrite of data). Output contains original paths for later restoration.
- `Check updates` shows CLI output and attempts to mark matching apps; it is best-effort, not a guarantee that all updates were found.
- The application is not a Windows service; the current actions run with the same rights as the app, and a package installer may request elevation.

## Next milestones

1. Structured WinGet provider and better installer identity.
2. Persistent SQLite store, with migration from `state.json`.
3. Configurable GitHub release manager and generic Scoop custom manifests.
4. Backup profile editor + Restore preview + verification.
5. Install queue, progress streaming and exporting a repeatable clean-Windows setup script.

## Source structure

```text
WindowsAppManager.csproj
App.xaml / App.xaml.cs
MainWindow.xaml / MainWindow.xaml.cs
Models/AppItem.cs
Services/AppStore.cs
Services/InventoryScanner.cs
Services/CommandRunner.cs
Services/BackupService.cs
Dialogs/AddAppDialog.xaml (.cs)
Dialogs/SearchDialog.xaml (.cs)
Dialogs/OutputDialog.xaml (.cs)
BackupProfiles/videoduplicatefinder.json
```
