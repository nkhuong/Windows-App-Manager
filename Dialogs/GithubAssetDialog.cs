using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsAppManager.Services;

namespace WindowsAppManager.Dialogs;

public sealed class GithubAssetDialog : Window
{
    public GithubAsset? SelectedAsset { get; private set; }
    public GithubAssetDialog(IReadOnlyList<GithubAsset> assets)
    {
        Title = "Choose GitHub release asset";
        Width = 680; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Surface");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "Select the Windows build matching your CPU (usually x64). ZIP / 7z only.", Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        var list = new ListBox { ItemsSource = assets, DisplayMemberPath = "Name",
            Background = Brushes.White, Foreground = Brushes.Black };
        Grid.SetRow(list, 1); grid.Children.Add(list);
        var add = new Button { Content = "Use selected asset", Margin = new Thickness(0, 12, 0, 0) };
        add.Click += (_, _) =>
        {
            if (list.SelectedItem is GithubAsset asset) { SelectedAsset = asset; DialogResult = true; }
        };
        Grid.SetRow(add, 2); grid.Children.Add(add);
        Content = grid;
    }
}
