# Integration Guide

This guide walks you through adding WpfMcpInspector to your WPF application.

## 1. Add the Project Reference

Add a reference to the WpfMcpInspector project (or NuGet package when available):

**Project reference (when building from source):**
```xml
<ItemGroup>
  <ProjectReference Include="path\to\WpfMcpInspector.csproj" />
</ItemGroup>
```

**NuGet (when published):**
```bash
dotnet add package WpfMcpInspector
```

## 2. Create and Start the Server

The `McpServer` must be created **after** your `MainWindow` is shown, because it needs a rendered visual tree to inspect.

**In App.xaml**, switch from `StartupUri` to a `Startup` event handler:

```xml
<Application x:Class="MyApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Startup="Application_Startup"
             Exit="Application_Exit">
```

**In App.xaml.cs:**

```csharp
using WpfMcpInspector;

public partial class App : Application
{
    private McpServer? _mcpServer;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();

        _mcpServer = new McpServer(window);
        _mcpServer.AppName = "MyApp";
        _mcpServer.Start();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _mcpServer?.Dispose();
    }
}
```

That is the minimal integration. The server will listen on `http://localhost:9222/` and expose all endpoints.

## 3. Register an AppStateProvider (Optional)

If you want the `GET /state` endpoint to return app-specific data, pass an `AppStateProvider` delegate:

```csharp
_mcpServer = new McpServer(window, mainWindow =>
{
    // This runs on the UI thread, so you can safely access UI elements
    var myWindow = (MainWindow)mainWindow;
    return new
    {
        CurrentDocument = myWindow.CurrentDocumentPath,
        UnsavedChanges = myWindow.HasUnsavedChanges,
        RecentFiles = myWindow.RecentFilesList.ToArray()
    };
});
```

The delegate receives the `Window` instance and must return a JSON-serializable object (or `null`).

## 4. Start/Stop Lifecycle

- **Start**: Call `server.Start()` after `window.Show()`. The visual tree must be rendered for inspection to work.
- **Stop**: Call `server.Dispose()` (or `server.Stop()`) during application exit.
- **Thread safety**: The server runs its HTTP listener on a background thread. All WPF access is marshalled via `Dispatcher.Invoke`, so your UI code does not need to be thread-safe.
- **Port selection**: The server tries ports 9222, 9223, and 9224 in order. If all are unavailable, it logs a warning and does not start (your app continues running normally).

## 5. Compile-Flag Gating

For production builds where you do not want the inspector embedded, use a compile flag:

**In your .csproj:**
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DefineConstants>$(DefineConstants);ENABLE_MCP</DefineConstants>
</PropertyGroup>
```

**In your App.xaml.cs:**
```csharp
private void Application_Startup(object sender, StartupEventArgs e)
{
    var window = new MainWindow();
    window.Show();

#if ENABLE_MCP
    _mcpServer = new McpServer(window);
    _mcpServer.AppName = "MyApp";
    _mcpServer.Start();
#endif
}

private void Application_Exit(object sender, ExitEventArgs e)
{
#if ENABLE_MCP
    _mcpServer?.Dispose();
#endif
}
```

This way the inspector code is only compiled into Debug builds. The WpfMcpInspector library itself is always compiled (it has no conditional compilation), but the `#if ENABLE_MCP` in your app prevents the server from being created or started in Release builds.

## 6. Naming Your Elements

The inspector identifies elements by `x:Name`. Give meaningful names to elements you want to interact with:

```xml
<TextBox x:Name="SearchBox" />
<Button x:Name="SearchButton" Content="Search" />
<ListView x:Name="ResultsList" />
<ComboBox x:Name="SortOrder" />
```

Elements without names can still be found by their integer ID (assigned automatically), but names are more readable and stable across sessions.

## 7. Verifying the Integration

After starting your app, verify the server is running:

```bash
# Check server health
curl http://localhost:9222/health

# List all interactable elements
curl http://localhost:9222/tree

# Inspect a specific element
curl http://localhost:9222/component/SearchBox

# Take a screenshot
curl http://localhost:9222/screenshot
```

## Real-World Example

[YTubeFetch](https://github.com/user/ytubefetcher) is a WPF video downloader that integrates WpfMcpInspector with compile-flag gating. Its `App.xaml.cs` creates the `McpServer` with an `AppStateProvider` that exposes download queue state, active jobs, and configuration -- enabling Claude Code to fully test the application's download workflow.
