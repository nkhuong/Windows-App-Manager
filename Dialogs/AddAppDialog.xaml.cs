using System.Windows;
using System.Windows.Controls;
using WindowsAppManager.Models;
namespace WindowsAppManager.Dialogs;

public partial class AddAppDialog : Window
{
    public PlannedApp? Result { get; private set; }
    public AddAppDialog() => InitializeComponent();

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string id = IdBox.Text.Trim();
        string source = ((ComboBoxItem)SourceBox.SelectedItem).Content.ToString() ?? "WinGet";
        if (source == "GitHub")
        {
            if (!Uri.TryCreate(id, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Trim('/').Split('/').Length != 2)
            {
                MessageBox.Show(this, "Enter a repository URL: https://github.com/owner/repository");
                return;
            }
            id = "https://github.com/" + uri.AbsolutePath.Trim('/');
            if (name.Length == 0) name = uri.AbsolutePath.Trim('/').Split('/')[1];
        }
        if (name.Length == 0 || id.Length == 0)
        {
            MessageBox.Show(this, "Enter display name and package ID/path.", "Missing fields");
            return;
        }
        Result = new PlannedApp { Name = name, PackageId = id, ManagedBy = source,
            InstallPath = source == "Manual" ? id : "" };
        DialogResult = true;
    }
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
