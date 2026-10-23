namespace CAP_Core.Export.Contracts;

/// <summary>Application-owned managed interpreter locations and creation defaults.</summary>
public sealed record ManagedPythonEnvironmentOptions(string EnvironmentsRoot,
    string DefaultPythonVersion = "3.11", string DefaultEnvironmentName = "nazca");
