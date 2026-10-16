using CAP.Avalonia.Services.Update;
using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Update;

/// <summary>Executes the generated script with fake process operations; never runs MSI or an app.</summary>
public class WindowsUpdaterExecutionTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(3010, false, true)]
    [InlineData(1603, false, false)]
    [InlineData(0, true, false)]
    [InlineData(-1, false, false)]
    public async Task Helper_WaitsInstallsAndRelaunchesOnlyAfterSuccess(int installerExit, bool stillRunning, bool relaunch)
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "lunima-fake-updater-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var trace = Path.Combine(directory, "trace.txt");
            var scriptPath = Path.Combine(directory, "fake.ps1");
            var prefix = $$"""
                $trace = {{UpdaterScripts.PsQuote(trace)}}
                function Record([string] $text) { [IO.File]::AppendAllText($trace, $text + [Environment]::NewLine) }
                # Define a harmless error dialog, then prevent the generated script loading WinForms.
                Add-Type 'namespace System.Windows.Forms { public static class MessageBox { public static void Show(string a, string b) {} } }'
                function Add-Type { param($AssemblyName) }
                function Out-File { param($FilePath, [switch]$Append) process {} }
                function Wait-Process { param($Id, $Timeout, $ErrorAction) Record 'wait' }
                function Get-Process { param($Id, $ErrorAction) if ({{(stillRunning ? "$true" : "$false")}}) { return @{ Id = $Id } } }
                function Start-Process {
                    param($FilePath, $ArgumentList, $Verb, [switch]$Wait, [switch]$PassThru)
                    if ($FilePath -eq 'msiexec') {
                        Record ('install:' + $Verb + ':' + $ArgumentList)
                        if ({{installerExit}} -eq -1) { throw 'UAC canceled' }
                        return [pscustomobject]@{ ExitCode = {{installerExit}} }
                    }
                    Record ('relaunch:' + $FilePath + ':verb=' + $Verb)
                }
                """;
            var target = new InstallLocation(@"C:\Program Files\Lunima", @"C:\Program Files\Lunima\CAP.Desktop.exe", 4242);
            File.WriteAllText(scriptPath, prefix + Environment.NewLine + UpdaterScripts.BuildWindows(target, @"C:\Downloads\O'Brien update.msi"));

            var result = await UvBootstrapper.RunProcessAsync(ProcessLaunchFactory.CreateDefault(),
                "powershell.exe", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath }, CancellationToken.None);

            var calls = File.ReadAllLines(trace);
            calls[0].ShouldBe("wait");
            calls.Any(c => c.StartsWith("install:")).ShouldBe(!stillRunning);
            calls.Any(c => c.StartsWith("relaunch:")).ShouldBe(relaunch);
            if (!stillRunning)
                calls[1].ShouldBe("install:RunAs:/i \"C:\\Downloads\\O'Brien update.msi\" /qb /norestart INSTALLDIR=\"C:\\Program Files\\Lunima\"");
            if (relaunch)
            {
                result.exitCode.ShouldBe(0, result.error);
                calls.Last().ShouldEndWith(":verb=");
            }
            else result.exitCode.ShouldNotBe(0);
        }
        finally { Directory.Delete(directory, true); }
    }
}
