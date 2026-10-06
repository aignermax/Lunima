using Avalonia.Media.Imaging;

namespace UnitTests.UI;

/// <summary>
/// Atomic file writes for UI-screenshot artifacts. QA/CI harnesses may run the
/// test suite and a dedicated UI-test step against the same checkout at the same
/// time; both executions write the same <c>artifacts/ui-screenshots/…</c> paths.
/// Writing directly (<c>bitmap.Save(path)</c> / <c>File.WriteAllText</c>) lets the
/// two processes collide on one file and fail whichever loses the race. Writing to
/// a unique temp file and renaming it into place is atomic on the same filesystem,
/// so concurrent runners can never observe (or produce) a partial artifact.
/// </summary>
internal static class ScreenshotArtifacts
{
    /// <summary>
    /// Set <c>CAP_UPDATE_PR_MEDIA=1</c> to refresh the published <c>docs/pr-media</c>
    /// screenshots; ordinary test runs write to a temp directory instead.
    /// </summary>
    public const string UpdatePrMediaVariable = "CAP_UPDATE_PR_MEDIA";

    /// <summary>
    /// Resolves the repo-root <c>docs/pr-media/{folderName}</c> directory (walking up from
    /// the test output for the .sln) — but only when the run opts into refreshing published
    /// PR media via <c>CAP_UPDATE_PR_MEDIA=1</c>. Otherwise a temp directory is returned so
    /// an ordinary test run still captures and validates frames but can never overwrite
    /// media that other issues' docs embed (a plain suite run re-rendering them with local
    /// fonts has silently reverted deliberate restores before).
    /// </summary>
    public static string ResolvePrMediaDirectory(string folderName)
    {
        if (Environment.GetEnvironmentVariable(UpdatePrMediaVariable) != "1")
            return Path.Combine(Path.GetTempPath(), "cap-pr-media", folderName);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "docs", "pr-media", folderName);
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "docs", "pr-media", folderName);
    }

    /// <summary>
    /// Saves <paramref name="bitmap"/> to <paramref name="path"/> atomically and
    /// returns the PNG bytes (read from the private temp file, so they can never
    /// reflect a concurrent writer's partial output).
    /// </summary>
    public static byte[] SavePng(Bitmap bitmap, string path)
    {
        var tmp = TempPathFor(path);
        try
        {
            bitmap.Save(tmp);
            var bytes = File.ReadAllBytes(tmp);
            File.Move(tmp, path, overwrite: true);
            return bytes;
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    /// <summary>Writes raw <paramref name="bytes"/> (e.g. a composed PNG) to <paramref name="path"/> atomically.</summary>
    public static void WriteBytes(string path, byte[] bytes)
    {
        var tmp = TempPathFor(path);
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/> atomically.</summary>
    public static void WriteText(string path, string content)
    {
        var tmp = TempPathFor(path);
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    private static string TempPathFor(string path) =>
        $"{path}.{Guid.NewGuid():N}.tmp";
}
