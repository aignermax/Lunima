namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>The <c>--option</c> part of a headless command line.</summary>
internal sealed class AgentCliOptions
{
    private string? _design;
    private string? _save;
    private string? _images;
    private bool _keepGoing;
    private bool _register;

    /// <summary>Splits <paramref name="args"/> into options and positional arguments.</summary>
    public bool TryRead(IReadOnlyList<string> args, List<string> positional, out string? error)
    {
        error = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--design":
                    if (!TryValue(args, ref i, arg, out _design, out error)) return false;
                    break;
                case "--images":
                    if (!TryValue(args, ref i, arg, out _images, out error)) return false;
                    break;
                case "--save":
                    // The file is optional: without it the design is saved back to --design.
                    _save = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        && args[i + 1].EndsWith(".lun", StringComparison.OrdinalIgnoreCase)
                        ? args[++i]
                        : string.Empty;
                    break;
                case "--keep-going":
                    _keepGoing = true;
                    break;
                case "--register":
                    _register = true;
                    break;
                case "--help" or "-h":
                    positional.Insert(0, "help");
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown option '{arg}'.";
                        return false;
                    }
                    positional.Add(arg);
                    break;
            }
        }
        return ValidateSave(out error);
    }

    /// <summary>Copies the options onto <paramref name="command"/>.</summary>
    public AgentCliCommand Apply(AgentCliCommand command) => command with
    {
        DesignPath = _design,
        SavePath = _save,
        ImageDirectory = _images,
        KeepGoing = _keepGoing,
        Register = _register,
    };

    private bool ValidateSave(out string? error)
    {
        error = _save == string.Empty && _design == null
            ? "--save without a file needs --design."
            : null;
        return error == null;
    }

    private static bool TryValue(IReadOnlyList<string> args, ref int i, string name, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (i + 1 >= args.Count)
        {
            error = $"{name} needs a value.";
            return false;
        }
        value = args[++i];
        return true;
    }
}
