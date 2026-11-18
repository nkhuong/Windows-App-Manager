using System.Windows;
namespace WindowsAppManager.Dialogs;
public partial class OutputDialog : Window
{
    public OutputDialog(string title, string text)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Output.Text = text;
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
