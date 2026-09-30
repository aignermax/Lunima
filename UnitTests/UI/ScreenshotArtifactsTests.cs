using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Pins the <c>docs/pr-media</c> write gate: a plain test run must never overwrite the
/// published PR media (a full-suite run once re-rendered another issue's screenshots and
/// silently reverted a deliberate restore). Only an explicit
/// <c>CAP_UPDATE_PR_MEDIA=1</c> opt-in may target the repo's docs folder.
/// </summary>
public class ScreenshotArtifactsTests
{
    [Fact]
    public void ResolvePrMediaDirectory_WithoutOptIn_StaysOutOfDocsPrMedia()
    {
        WithUpdatePrMedia(null, () =>
        {
            var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-9999");
            dir.ShouldNotContain(Path.Combine("docs", "pr-media"));
            dir.ShouldStartWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        });
    }

    [Fact]
    public void ResolvePrMediaDirectory_WithOptIn_PointsAtRepoDocsPrMedia()
    {
        WithUpdatePrMedia("1", () =>
        {
            var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-9999");
            Path.GetFileName(dir).ShouldBe("issue-9999");
            Path.GetFileName(Path.GetDirectoryName(dir)).ShouldBe("pr-media");
        });
    }

    private static void WithUpdatePrMedia(string? value, Action assert)
    {
        var previous = Environment.GetEnvironmentVariable(ScreenshotArtifacts.UpdatePrMediaVariable);
        Environment.SetEnvironmentVariable(ScreenshotArtifacts.UpdatePrMediaVariable, value);
        try
        {
            assert();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotArtifacts.UpdatePrMediaVariable, previous);
        }
    }
}
