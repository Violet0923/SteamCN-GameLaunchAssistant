using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WetheringWavesSteamHelper_WinUI.Views.Pages;

public sealed partial class EmptyGameLibraryPage : Page
{
    public event RoutedEventHandler? AddGameRequested;

    public EmptyGameLibraryPage()
    {
        InitializeComponent();
    }

    private void AddGame_Click(object sender, RoutedEventArgs e) => AddGameRequested?.Invoke(this, e);
}
