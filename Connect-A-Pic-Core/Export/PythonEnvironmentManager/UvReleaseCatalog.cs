using System.Runtime.InteropServices;

namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>
/// Reviewed release checksums from https://github.com/astral-sh/uv/releases/tag/0.13.5
/// and its linked *.sha256 assets (2026-10-23). Updating this table requires review.
/// </summary>
internal static class UvReleaseCatalog
{
    internal const string Version = "0.13.5";

    internal static UvReleaseArtifact Current => Resolve(
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "unsupported", RuntimeInformation.ProcessArchitecture);

    internal static UvReleaseArtifact Resolve(string platform, Architecture architecture)
    {
        var (target, extension, hash) = (platform, architecture) switch
        {
            ("windows", Architecture.X64) => ("x86_64-pc-windows-msvc", ".zip", "a38b8d1902b7cfbc76b423ba1bb456f623f42064cdc72628b6075b89afd1019a"),
            ("windows", Architecture.Arm64) => ("aarch64-pc-windows-msvc", ".zip", "09f47edf90726084a70f9426f6e3ca4f46e9bf7cbf23b1716ec64d60b055fae3"),
            ("macos", Architecture.Arm64) => ("aarch64-apple-darwin", ".tar.gz", "743f85035a1996ecb350659e01cd9faea9709829be09b1b4b03c73eaf3c9cd73"),
            ("macos", Architecture.X64) => ("x86_64-apple-darwin", ".tar.gz", "b0f52b0e3945b4107d89024ea38e625683011fbfc05a0d98f9fa2d6dcee28c66"),
            ("linux", Architecture.Arm64) => ("aarch64-unknown-linux-musl", ".tar.gz", "7906c79adb21c81abd559841ba76bb1d9af9d791acac7a041b2972e03ae16538"),
            ("linux", Architecture.X64) => ("x86_64-unknown-linux-musl", ".tar.gz", "57ef9d9ccb01fb1bd998df995b0dd221b52f7d7841fbea93f2d4463bf61fb7a9"),
            _ => throw new PlatformNotSupportedException($"Managed uv is not available for {platform}/{architecture}.")
        };
        return new(Version, target, "uv-" + target + extension, hash, platform == "windows" ? "uv.exe" : "uv");
    }
}
