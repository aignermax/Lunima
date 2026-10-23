using CAP_Core.ComponentRegistry.RegistryClient;

namespace CAP.Avalonia.Services.ComponentRegistry;

/// <summary>
/// The S-parameter artifact of a registry component chosen for adoption into a
/// local PDK: its tier (<c>simulated</c> or <c>measured</c>), the artifact
/// itself, and whether it carries the <c>disputed</c> trust status (which
/// requires an explicit user confirmation before downloading, issue #773).
/// </summary>
public sealed record RegistryArtifactChoice(string Tier, ArtifactRef Artifact, bool IsDisputed);

