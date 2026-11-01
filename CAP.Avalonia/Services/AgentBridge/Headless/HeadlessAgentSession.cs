using System.Reflection;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP_Core;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>
/// The production application without a window: the real DI container
/// (<see cref="App.ConfigureServices"/>) and its <see cref="MainViewModel"/>, driven only by
/// agent tools. Every tool therefore runs the same code path as in the desktop app.
/// <para>
/// Preferences are read from a private copy of the user's file, so the session sees the
/// user's PDKs, Python interpreter and routing settings, but nothing it does (recent files,
/// reopen-last-project) leaks back into the interactive app.
/// </para>
/// Must be created and used on the Avalonia UI thread of an initialised (headless) platform.
/// </summary>
public sealed class HeadlessAgentSession : IAsyncDisposable
{
    private const string AgentLanguage = "en";

    private readonly ServiceProvider _provider;
    private readonly string _preferencesCopy;

    private HeadlessAgentSession(ServiceProvider provider, string preferencesCopy)
    {
        _provider = provider;
        _preferencesCopy = preferencesCopy;
        Main = provider.GetRequiredService<MainViewModel>();
        Catalog = provider.GetRequiredService<AgentToolCatalog>();
        Dispatcher = new McpJsonRpcDispatcher(Catalog, AppVersion(), McpJsonRpcDispatcher.HeadlessInstructions);
    }

    /// <summary>The production container behind this session.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>The application's main view-model (the open design and every panel).</summary>
    public MainViewModel Main { get; }

    /// <summary>All agent tools, executed on the UI thread.</summary>
    public AgentToolCatalog Catalog { get; }

    /// <summary>MCP JSON-RPC handling over <see cref="Catalog"/>, for any transport.</summary>
    public McpJsonRpcDispatcher Dispatcher { get; }

    /// <summary>Builds the container and the main view-model.</summary>
    /// <param name="diagnostics">Receives every error-console entry (the host writes them to stderr).</param>
    public static HeadlessAgentSession Create(TextWriter diagnostics)
    {
        var preferencesCopy = CopyUserPreferences();
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        services.AddSingleton(_ => new UserPreferencesService(preferencesCopy));
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ErrorConsoleService>().EntryAdded +=
            (_, entry) => diagnostics.WriteLine($"[lunima {entry.Level}] {entry.Message}");
        provider.GetRequiredService<LocalizationService>().SetLanguage(AgentLanguage);
        return new HeadlessAgentSession(provider, preferencesCopy);
    }

    /// <summary>Opens a <c>.lun</c> design and waits until its routes are recalculated.</summary>
    /// <exception cref="InvalidOperationException">The design could not be loaded.</exception>
    public async Task OpenDesignAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!await Main.FileOperations.LoadDesignFromPathAsync(fullPath))
            throw new InvalidOperationException($"Could not open design '{fullPath}'.");
        await Main.FileOperations.PostLoadRouting;
    }

    /// <summary>Saves the open design to <paramref name="path"/>.</summary>
    /// <exception cref="InvalidOperationException">The design could not be written.</exception>
    public async Task SaveDesignAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!await Main.FileOperations.SaveDesignToPathAsync(fullPath))
            throw new InvalidOperationException($"Could not save design to '{fullPath}'.");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try { await Main.DisposeAsync(); }
        finally
        {
            await _provider.DisposeAsync();
            TryDelete(_preferencesCopy);
        }
    }

    private static string CopyUserPreferences()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"lunima-agent-{Guid.NewGuid():N}.json");
        var source = UserPreferencesService.DefaultFilePath;
        if (File.Exists(source))
            File.Copy(source, copy);
        return copy;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string AppVersion() =>
        (Assembly.GetEntryAssembly() ?? typeof(HeadlessAgentSession).Assembly)
            .GetName().Version?.ToString(3) ?? "0.0.0";
}
