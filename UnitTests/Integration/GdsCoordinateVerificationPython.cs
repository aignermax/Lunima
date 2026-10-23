using System.Diagnostics;
using System.Globalization;

namespace UnitTests.Integration;

/// <summary>Optional Python environment checks and GDS comparison process support.</summary>
internal static class GdsCoordinateVerificationPython
{
    internal static async Task<bool> CheckGdspyAsync()
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "check_gdspy_cap.py");
            await File.WriteAllTextAsync(tmp, "import gdspy; print('ok')");
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{tmp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (File.Exists(tmp)) File.Delete(tmp);
            return proc.ExitCode == 0 && output.Trim() == "ok";
        }
        catch { return false; }
    }

    internal static async Task RunPythonAsync(string scriptPath, string args)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            await proc.WaitForExitAsync();
        }
        catch { /* Python not available */ }
    }

    internal static async Task<(int exitCode, string output, string error)> RunPythonRawAsync(
        string scriptPath, string args)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, await outTask, await errTask);
        }
        catch (Exception ex) { return (1, "", ex.Message); }
    }

    internal static string FixGdsOutputPath(string script, string gdsPath)
        => script.Replace(
            "gds_filename = os.path.splitext(script_path)[0] + '.gds'",
            $"gds_filename = r'{gdsPath.Replace("\\", "/")}'");

    internal static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "ConnectAPICPro.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("ConnectAPICPro.sln not found");
    }

    internal static double ParseMaxDeviation(string reportJson)
    {
        const string key = "\"max_deviation_um\":";
        int idx = reportJson.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return double.MaxValue;
        int start = idx + key.Length;
        while (start < reportJson.Length && char.IsWhiteSpace(reportJson[start])) start++;
        int end = start;
        while (end < reportJson.Length &&
               (char.IsDigit(reportJson[end]) || reportJson[end] == '.' || reportJson[end] == '-'))
            end++;
        return double.TryParse(reportJson.AsSpan(start, end - start),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : double.MaxValue;
    }
}
