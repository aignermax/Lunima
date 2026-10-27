using System.Diagnostics;
using System.Text;

namespace CAP.Avalonia.Services.AgentBridge.Registration;

/// <summary>Explicit cmd invocation for npm-style batch forwarders, retaining structured CLI arguments.</summary>
internal static class WindowsBatchCommand
{
    internal static ProcessStartInfo Prepare(ProcessStartInfo source)
    {
        if (!OperatingSystem.IsWindows()
            || !(Path.GetExtension(source.FileName).Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                 || Path.GetExtension(source.FileName).Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            return source;

        if (!string.IsNullOrEmpty(source.Arguments))
            throw new InvalidOperationException("Batch CLI execution requires structured ArgumentList values.");
        Validate(source.FileName);
        foreach (var argument in source.ArgumentList) Validate(argument);

        // The first cmd parse invokes the shim. Its %* then feeds another command parse
        // before the native tool receives CRT argv. Protect metacharacters at both layers.
        // Reference model: https://github.com/moxystudio/node-cross-spawn/blob/master/lib/util/escape.js
        var command = new StringBuilder(EscapeShell(source.FileName));
        foreach (var argument in source.ArgumentList)
            command.Append(' ').Append(EscapeShell(EscapeShell(QuoteNativeArgument(argument))));
        var arguments = $"/d /s /v:off /c \"{command}\"";
        if (arguments.Length > 8191)
            throw new InvalidOperationException("Batch command exceeds cmd.exe's 8191-character limit.");

        var prepared = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            // Shell syntax is intentional here; CRT ArgumentList serialization would
            // reinterpret the already escaped quotes and corrupt the shell command.
            Arguments = arguments,
            WorkingDirectory = source.WorkingDirectory,
            UseShellExecute = source.UseShellExecute,
            CreateNoWindow = source.CreateNoWindow,
            RedirectStandardInput = source.RedirectStandardInput,
            RedirectStandardOutput = source.RedirectStandardOutput,
            RedirectStandardError = source.RedirectStandardError,
            StandardInputEncoding = source.StandardInputEncoding,
            StandardOutputEncoding = source.StandardOutputEncoding,
            StandardErrorEncoding = source.StandardErrorEncoding,
            UserName = source.UserName
        };
        prepared.Environment.Clear();
        foreach (var entry in source.Environment) prepared.Environment[entry.Key] = entry.Value;
        return prepared;
    }

    private static void Validate(string value)
    {
        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new InvalidOperationException("Batch arguments cannot contain NUL, CR, or LF.");
    }

    private static string EscapeShell(string value)
    {
        var escaped = new StringBuilder(value.Length * 2);
        foreach (var character in value)
        {
            if ("()[]%!^\"`<>&|;, *?".Contains(character)) escaped.Append('^');
            escaped.Append(character);
        }
        return escaped.ToString();
    }

    private static string QuoteNativeArgument(string value)
    {
        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(character);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
