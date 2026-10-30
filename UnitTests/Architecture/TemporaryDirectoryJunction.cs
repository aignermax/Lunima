using System.Diagnostics;
using CAP_Core.Export.Processes;
using Shouldly;

namespace UnitTests.Architecture;

/// <summary>Privilege-free Windows directory aliases confined to one test-owned directory.</summary>
internal static class TemporaryDirectoryJunction
{
    public static async Task CreateAsync(string fixtureRoot, string alias, string target)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = Path.GetFullPath(fixtureRoot);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("lunima-pdk-alias-", StringComparison.Ordinal))
            throw new InvalidOperationException("The junction fixture must be an owned temporary directory.");
        alias = ValidateChild(root, alias);
        target = ValidateChild(root, target);
        if (!Directory.Exists(target) || Directory.Exists(alias) || File.Exists(alias))
            throw new InvalidOperationException("Junction creation requires an existing target and an unused alias.");

        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        {
            // cmd parses shell syntax, not CRT ArgumentList escaping. All substituted paths
            // are validated below, quoted individually, and cannot contain expansion syntax.
            Arguments = $"/d /v:off /c mklink /J \"{alias}\" \"{target}\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var process = OwnedProcess.Start(start);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            try { await process.StopAsync(); }
            catch
            {
                try { await process.DisposeAsync(); }
                finally
                {
                    try { await Task.WhenAll(stdout, stderr); }
                    catch { /* Observe closed readers while preserving the retirement failure. */ }
                }
                throw;
            }
            await Task.WhenAll(stdout, stderr);
        }
        process.ExitCode.ShouldBe(0, $"mklink failed: {await stdout}\n{await stderr}");
        Directory.Exists(alias).ShouldBeTrue();
        new DirectoryInfo(alias).ResolveLinkTarget(true)!.FullName.ShouldBe(target);
    }

    private static string ValidateChild(string root, string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || full.IndexOfAny(['"', '%', '!', '&', '|', '<', '>', '^', '\r', '\n']) >= 0)
            throw new InvalidOperationException("Junction paths must be literal children of the owned fixture directory.");
        return full;
    }
}
