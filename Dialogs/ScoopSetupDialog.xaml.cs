using System.Windows;
using Microsoft.Win32;

namespace WindowsAppManager.Dialogs;

public partial class ScoopSetupDialog : Window
{
    public string InstallRoot { get; private set; } = "";
    public ScoopSetupDialog(string suggestedRoot)
    {
        InitializeComponent();
        RootBox.Text = suggestedRoot;
    }
    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose an empty Scoop folder" };
        if (picker.ShowDialog(this) == true) RootBox.Text = picker.FolderName;
    }
    private void InstallClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = RootBox.Text.Trim();
            if (!Path.IsPathFullyQualified(path)) throw new IOException("Enter an absolute folder path, for example D:\\PortableApps\\Scoop.");
            path = Path.GetFullPath(path);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
                throw new IOException("Choose an empty folder to preserve existing files.");
            if (File.Exists(path)) throw new IOException("This path is a file. Choose a folder.");
            InstallRoot = path;
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Scoop folder", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
