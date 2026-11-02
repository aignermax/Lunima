using CAP_Core.Export.Processes;

namespace CAP_Core.Export.FoundryDrc;

/// <summary>Outcome of one foundry DRC run.</summary>
/// <param name="Ran">Whether KLayout ran the deck and produced a report.</param>
/// <param name="Error">Why it did not run, or null.</param>
/// <param name="Rules">Every checked rule with its violation count.</param>
/// <param name="Markers">Every violation marker.</param>
public sealed record FoundryDrcResult(bool Ran, string? Error, IReadOnlyList<DrcRuleSummary> Rules, IReadOnlyList<DrcMarker> Markers)
{
    /// <summary>A run that did not happen.</summary>
    public static FoundryDrcResult Failed(string error) => new(false, error, Array.Empty<DrcRuleSummary>(), Array.Empty<DrcMarker>());
}

/// <summary>
/// Runs a KLayout DRC deck (<c>.lydrc</c>) on a GDS in batch mode —
/// <c>klayout -b -r deck -rd input=gds -rd report=lyrdb</c> — and parses the marker database.
/// KLayout exits 0 even when rules fail, so the verdict always comes from the report.
/// Shipped decks live in <c>scripts/drc/</c> beside the application.
/// </summary>
public sealed class KLayoutDrcRunner : IFoundryDrcRunner
{
    private static readonly string[] Executables = { "klayout", "klayout_app" };
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly ProcessLaunchFactory _launch;
    private readonly TimeSpan _timeout;

    /// <summary>Creates the runner.</summary>
    public KLayoutDrcRunner(ProcessLaunchFactory? launch = null, TimeSpan? timeout = null)
    {
        _launch = launch ?? ProcessLaunchFactory.CreateDefault();
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>The decks shipped in <c>scripts/drc</c> (name → path), found beside the app or above it.</summary>
    public IReadOnlyDictionary<string, string> ShippedDecks()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var decks = Path.Combine(dir.FullName, "scripts", "drc");
                if (Directory.Exists(decks))
                    return Directory.GetFiles(decks, "*.lydrc")
                        .ToDictionary(path => Path.GetFileNameWithoutExtension(path), path => path, StringComparer.OrdinalIgnoreCase);
            }
        }
        return new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public async Task<FoundryDrcResult> RunAsync(string gdsPath, string deckPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gdsPath)) return FoundryDrcResult.Failed($"GDS not found: {gdsPath}");
        if (!File.Exists(deckPath)) return FoundryDrcResult.Failed($"DRC deck not found: {deckPath}");
        var report = Path.Combine(Path.GetTempPath(), $"lunima-drc-{Guid.NewGuid():N}.lyrdb");
        var arguments = new[] { "-b", "-r", deckPath, "-rd", $"input={gdsPath}", "-rd", $"report={report}" };
        try
        {
            CapturedProcessResult? run = null;
            foreach (var executable in Candidates())
            {
                if (!_launch.TryBuild(executable, arguments, Path.GetDirectoryName(gdsPath), null, out var start, out _))
                    continue;
                try { run = await OwnedProcessCapture.RunAsync(start, _timeout, cancellationToken); break; }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (run == null)
                return FoundryDrcResult.Failed("KLayout was not found. Install KLayout (klayout.de) or set KLAYOUT to its executable.");
            if (run.Value.TimedOut)
                return FoundryDrcResult.Failed($"KLayout timed out after {_timeout.TotalMinutes:F0} min.");
            if (!File.Exists(report))
                return FoundryDrcResult.Failed($"KLayout wrote no report (exit {run.Value.ExitCode}): {Tail(run.Value.Error + run.Value.Output)}");
            var (rules, markers) = DrcMarkerDatabase.Parse(await File.ReadAllTextAsync(report, cancellationToken));
            return new FoundryDrcResult(true, null, rules, markers);
        }
        catch (FormatException ex) { return FoundryDrcResult.Failed(ex.Message); }
        finally
        {
            try { File.Delete(report); } catch (IOException) { }
        }
    }

    private static IEnumerable<string> Candidates()
    {
        var configured = Environment.GetEnvironmentVariable("KLAYOUT");
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
        foreach (var name in Executables) yield return name;
    }

    private static string Tail(string text) => text.Length <= 400 ? text.Trim() : text[^400..].Trim();
}
