using CAP.Avalonia.Services;
using Shouldly;

namespace UnitTests.Update;

/// <summary>
/// Unit tests for the "Skip for Today" feature in UserPreferencesService.
/// Verifies daily skip persistence, reset after a day, and independence from version skip.
/// </summary>
public class UserPreferencesSkipTodayTests
{
    [Fact]
    public void ShouldCheckToday_Default_ReturnsTrue()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.ShouldCheckToday().ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SkipToday_SameDay_ShouldCheckTodayReturnsFalse()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);

            prefs.SkipToday();

            prefs.ShouldCheckToday().ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SkipToday_PersistsToDisk_SurvivesReload()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs1 = new UserPreferencesService(tempPath);
            prefs1.SkipToday();

            // Reload from disk
            var prefs2 = new UserPreferencesService(tempPath);
            prefs2.ShouldCheckToday().ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ShouldCheckToday_PastDate_ReturnsTrue()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            // Write a preference file with yesterday's date
            var yesterday = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
            var json = $$"""
                {
                  "SkippedTodayDate": "{{yesterday}}"
                }
                """;
            File.WriteAllText(tempPath, json);

            var prefs = new UserPreferencesService(tempPath);
            prefs.ShouldCheckToday().ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SkipToday_DoesNotAffectSkippedVersion()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            var version = new CAP_Core.Update.SemanticVersion(2, 0, 0);
            prefs.SetSkippedUpdateVersion(version);

            prefs.SkipToday();

            // Version skip should still be set
            prefs.GetSkippedUpdateVersion().ShouldNotBeNull();
            prefs.GetSkippedUpdateVersion()!.Major.ShouldBe(2);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ShouldCheckToday_InvalidDate_ReturnsTrue()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var json = """
                {
                  "SkippedTodayDate": "not-a-date"
                }
                """;
            File.WriteAllText(tempPath, json);

            var prefs = new UserPreferencesService(tempPath);
            prefs.ShouldCheckToday().ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
