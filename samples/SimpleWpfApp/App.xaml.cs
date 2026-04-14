using System.Windows;
using WpfMcpInspector;

namespace SimpleWpfApp;

public partial class App : Application
{
    private McpServer? _mcpServer;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();

        _mcpServer = new McpServer(window);
        _mcpServer.AppName = "SimpleWpfApp";
        _mcpServer.Start();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _mcpServer?.Dispose();
    }
}
