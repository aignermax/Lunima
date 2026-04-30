using CAP.Avalonia.Services;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests for <see cref="UserSMatrixOverrideStore"/>: round-trip persistence,
/// graceful behaviour on missing/corrupt files, and re-load semantics.
/// Each test uses an isolated temp file so tests run in parallel without
/// interfering with each other or with the real user-global store.
/// </summary>
public class UserSMatrixOverrideStoreTests : IDisposable
{
    private readonly string _tempPath;

    public UserSMatrixOverrideStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"sparam-overrides-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    private static ComponentSMatrixData MakeSimple2x2(string sourceNote = "Test")
    {
        var data = new ComponentSMatrixData { SourceNote = sourceNote };
        data.Wavelengths["1550"] = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            Real = new List<double> { 0.9, 0.1, 0.1, 0.9 },
            Imag = new List<double> { 0, 0, 0, 0 },
            PortNames = new List<string> { "in", "out" }
        };
        return data;
    }

    [Fact]
    public void Constructor_NoExistingFile_StartsEmpty()
    {
        var store = new UserSMatrixOverrideStore(_tempPath);

        store.Overrides.Count.ShouldBe(0);
    }

    [Fact]
    public void Save_ThenReload_RestoresOverrides()
    {
        var store = new UserSMatrixOverrideStore(_tempPath);
        store.Apply("siepic-ebeam-pdk::2x2 MMI Coupler", MakeSimple2x2("Lumerical bdc_te1550.sparam"));
        store.Save();

        File.Exists(_tempPath).ShouldBeTrue();

        var fresh = new UserSMatrixOverrideStore(_tempPath);
        fresh.Overrides.Count.ShouldBe(1);
        fresh.Overrides.ShouldContainKey("siepic-ebeam-pdk::2x2 MMI Coupler");
        fresh.Overrides["siepic-ebeam-pdk::2x2 MMI Coupler"].SourceNote.ShouldBe("Lumerical bdc_te1550.sparam");
        fresh.Overrides["siepic-ebeam-pdk::2x2 MMI Coupler"].Wavelengths["1550"].Rows.ShouldBe(2);
    }

    [Fact]
    public void Save_CreatesParentDirectoryWhenMissing()
    {
        var nestedPath = Path.Combine(
            Path.GetTempPath(),
            $"sparam-test-{Guid.NewGuid()}",
            "deep",
            "sparam-overrides.json");
        try
        {
            var store = new UserSMatrixOverrideStore(nestedPath);
            store.Apply("any::key", MakeSimple2x2());
            store.Save();

            File.Exists(nestedPath).ShouldBeTrue();
        }
        finally
        {
            var rootDir = Path.GetDirectoryName(Path.GetDirectoryName(nestedPath));
            if (rootDir != null && Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public void Remove_DeletesFromMemory_NotPersistedUntilSave()
    {
        var store = new UserSMatrixOverrideStore(_tempPath);
        store.Apply("k1", MakeSimple2x2());
        store.Apply("k2", MakeSimple2x2());
        store.Save();

        store.Remove("k1").ShouldBeTrue();
        store.Overrides.Count.ShouldBe(1);

        // On disk still has both — Remove without Save shouldn't persist.
        var diskState = new UserSMatrixOverrideStore(_tempPath);
        diskState.Overrides.Count.ShouldBe(2);

        store.Save();
        var afterSave = new UserSMatrixOverrideStore(_tempPath);
        afterSave.Overrides.Count.ShouldBe(1);
    }

    [Fact]
    public void Load_CorruptJsonFile_LeavesStoreEmpty_DoesNotThrow()
    {
        // A corrupt file must not crash app startup. Surface via ErrorConsoleService.
        File.WriteAllText(_tempPath, "{ this is not valid json");

        var store = new UserSMatrixOverrideStore(_tempPath);

        // Constructor must not throw; store stays empty.
        store.Overrides.Count.ShouldBe(0);
    }

    [Fact]
    public void Reload_DiscardsUnsavedMutations()
    {
        var store = new UserSMatrixOverrideStore(_tempPath);
        store.Apply("persisted", MakeSimple2x2("on disk"));
        store.Save();

        store.Apply("ephemeral", MakeSimple2x2("not saved"));
        store.Overrides.Count.ShouldBe(2);

        store.Reload();

        store.Overrides.Count.ShouldBe(1);
        store.Overrides.ShouldContainKey("persisted");
        store.Overrides.ShouldNotContainKey("ephemeral");
    }
}
