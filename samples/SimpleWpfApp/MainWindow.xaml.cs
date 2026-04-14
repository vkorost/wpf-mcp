using System.Windows;

namespace SimpleWpfApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = $"Submitted: {InputBox.Text}";
    }

    private void MenuNew_Click(object sender, RoutedEventArgs e)
    {
        InputBox.Clear();
        StatusText.Text = "New document started";
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Open dialog would appear here";
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Simple WPF Demo App\nUsing WpfMcpInspector for UI inspection.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
