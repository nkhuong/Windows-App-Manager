using System.ComponentModel;
using System.Windows;
namespace WindowsAppManager.Dialogs;
public partial class OutputDialog : Window
{
    private bool running;
    public OutputDialog(string title, string text)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Output.Text = text;
        Closing += OnClosing;
    }
    public void BeginRunning()
    {
        running = true;
        CloseButton.IsEnabled = false;
        CloseButton.Content = "Running...";
    }
    public void AppendOutput(string text)
    {
        Output.AppendText(text);
        Output.ScrollToEnd();
    }
    public void Complete(string title)
    {
        // Post after pending Progress callbacks, so trailing output stays ahead of the completion marker.
        Dispatcher.InvokeAsync(() =>
        {
            Title = Heading.Text = title;
            AppendOutput("\n" + title + "\n");
            running = false;
            CloseButton.Content = "Close";
            CloseButton.IsEnabled = true;
        });
    }
    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = running;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
