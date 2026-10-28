using System.Diagnostics;
using System.Text;

namespace CAP_Core.Export.Processes;

internal static class ProcessArguments
{
    internal static IReadOnlyList<string> Read(ProcessStartInfo info)
    {
        if (info.ArgumentList.Count > 0)
        {
            if (!string.IsNullOrEmpty(info.Arguments))
                throw new InvalidOperationException("Use either ArgumentList or Arguments, not both.");
            return info.ArgumentList.ToArray();
        }
        var result = new List<string>();
        var text = info.Arguments;
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (text[index] is ' ' or '\t')) index++;
            if (index == text.Length) break;
            var argument = new StringBuilder();
            var quoted = false;
            while (index < text.Length && (quoted || !(text[index] is ' ' or '\t')))
            {
                var slashes = 0;
                while (index < text.Length && text[index] == '\\') { slashes++; index++; }
                if (index < text.Length && text[index] == '"')
                {
                    argument.Append('\\', slashes / 2);
                    if (slashes % 2 != 0) argument.Append('"');
                    else if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                    { argument.Append('"'); index++; }
                    else quoted = !quoted;
                    index++;
                }
                else
                {
                    argument.Append('\\', slashes);
                    if (index < text.Length && (quoted || !(text[index] is ' ' or '\t')))
                        argument.Append(text[index++]);
                }
            }
            result.Add(argument.ToString());
        }
        return result;
    }

    internal static string WindowsCommandLine(ProcessStartInfo info)
    {
        var executable = Quote(info.FileName);
        if (info.ArgumentList.Count == 0)
            return string.IsNullOrEmpty(info.Arguments) ? executable : executable + " " + info.Arguments;
        return executable + " " + string.Join(" ", Read(info).Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        // Match System.Diagnostics.Process/.NET 10 PasteArguments.AppendArgument:
        // simple arguments remain unquoted. cmd.exe parses quoted internal command
        // names differently, even though a conventional argv parser would not.
        // https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/PasteArguments.cs
        return value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"')
            ? value : Quote(value);
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
