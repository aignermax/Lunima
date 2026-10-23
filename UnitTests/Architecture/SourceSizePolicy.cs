using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnitTests.Architecture;

/// <summary>Reads the same authored-source policy used by CI and the staged-index hook.</summary>
internal sealed class SourceSizePolicy
{
    [JsonPropertyName("max_lines")] public int MaxLines { get; init; }
    [JsonPropertyName("source_suffixes")] public string[] SourceSuffixes { get; init; } = [];
    [JsonPropertyName("source_names")] public string[] SourceNames { get; init; } = [];
    [JsonPropertyName("source_prefixes")] public string[] SourcePrefixes { get; init; } = [];

    public static SourceSizePolicy Load(string root) => JsonSerializer.Deserialize<SourceSizePolicy>(
        File.ReadAllText(Path.Combine(root, "scripts", "source_size_policy.json")))
        ?? throw new InvalidDataException("The canonical source-size policy is missing or invalid.");

    public bool IsSource(string path) => SourceSuffixes.Contains(Path.GetExtension(path).ToLowerInvariant()) ||
        SourceNames.Contains(Path.GetFileName(path)) || SourcePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));

    public string? Violation(string path, byte[] bytes)
    {
        if (!IsSource(path)) return null;
        if (bytes.Contains((byte)0)) return $"{path}: binary content in a source file";
        var count = PhysicalLines(bytes);
        return count > MaxLines ? $"{path}: {count} lines (maximum {MaxLines})" : null;
    }

    public static int PhysicalLines(byte[] bytes) => bytes.Count(value => value == (byte)'\n') +
        (bytes.Length > 0 && bytes[^1] != (byte)'\n' ? 1 : 0);

    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var git = Path.Combine(directory.FullName, ".git");
            if (File.Exists(git) || Directory.Exists(git)) return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate repository root (.git directory or worktree file).");
    }

    public static async Task<string[]> TrackedFilesAsync(string root)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-C", root, "ls-files", "-z" }) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
            return (await output).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
            finally
            {
                // Reads share the bounded operation token; observe both before disposing their streams.
                timeout.Cancel();
                try { await Task.WhenAll(output, error); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                catch (IOException) { /* The killed process may close a pipe while a read is pending. */ }
            }
        }
    }
}
