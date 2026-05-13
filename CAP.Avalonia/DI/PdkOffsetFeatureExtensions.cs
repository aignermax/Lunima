using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the PDK offset editor feature: saver, Nazca preview service, and ViewModel.
/// The ViewModel is Singleton so that MainViewModel always receives the same instance —
/// this preserves editor state (loaded PDK, unsaved edits) when the user re-opens the window.
/// </summary>
internal static class PdkOffsetFeatureExtensions
{
    /// <summary>
    /// Adds PDK JSON saving, the Nazca component preview service, and the
    /// <see cref="PdkOffsetEditorViewModel"/> as a singleton.
    /// </summary>
    public static IServiceCollection AddPdkOffsetFeature(this IServiceCollection services)
    {
        services.AddSingleton<PdkJsonSaver>();
        services.AddSingleton(sp =>
        {
            var prefs = sp.GetRequiredService<UserPreferencesService>();
            var python = prefs.GetCustomPythonPath() ?? ResolvePythonExecutable();
            var script = FindPreviewScript();
            return new NazcaComponentPreviewService(python, script);
        });
        services.AddSingleton(sp => new PdkOffsetEditorViewModel(
            sp.GetRequiredService<PdkLoader>(),
            sp.GetRequiredService<PdkJsonSaver>(),
            sp.GetRequiredService<PdkManagerViewModel>(),
            sp.GetRequiredService<NazcaComponentPreviewService>()));

        return services;
    }

    /// <summary>
    /// Searches for render_component_preview.py relative to the application base directory.
    /// Returns the first candidate path found, or the primary candidate when none exist.
    /// </summary>
    private static string FindPreviewScript()
    {
        const string ScriptName = "render_component_preview.py";
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var local = Path.Combine(baseDir, "scripts", ScriptName);
        if (File.Exists(local)) return local;

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", ScriptName);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        return local;
    }

    /// <summary>
    /// Picks the first runnable Python interpreter from a per-platform candidate list.
    /// </summary>
    private static string ResolvePythonExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "python", "py", "python3" }
            : new[] { "python3", "python" };

        foreach (var candidate in candidates)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (p == null) continue;
                p.WaitForExit(2000);
                if (p.ExitCode == 0) return candidate;
            }
            catch (Win32Exception) { }
            catch (Exception) { }
        }

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }
}
