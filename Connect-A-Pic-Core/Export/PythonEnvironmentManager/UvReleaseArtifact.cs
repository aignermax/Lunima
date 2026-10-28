namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>A reviewed upstream archive identity; never populated from downloaded metadata.</summary>
internal sealed record UvReleaseArtifact(string Version, string Platform, string FileName, string Sha256, string ExecutableName)
{
    public string Url => $"https://releases.astral.sh/github/uv/releases/download/{Version}/{FileName}";
}
