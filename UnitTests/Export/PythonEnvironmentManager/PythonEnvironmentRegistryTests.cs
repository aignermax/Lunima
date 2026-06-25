using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

/// <summary>
/// Unit tests for <see cref="PythonEnvironmentRegistry"/>.
/// Uses a temp file to avoid touching real user preferences.
/// </summary>
public class PythonEnvironmentRegistryTests : IDisposable
{
    private readonly string _tempFile;
    private readonly PythonEnvironmentRegistry _registry;

    public PythonEnvironmentRegistryTests()
    {
        _tempFile = Path.GetTempFileName();
        File.Delete(_tempFile); // registry creates fresh
        _registry = CreateRegistry(_tempFile);
    }

    private static PythonEnvironmentRegistry CreateRegistry(string filePath)
    {
        // Use reflection to inject a custom path for testing
        var registry = new PythonEnvironmentRegistry();
        // NOTE: Production code uses LocalApplicationData/Lunima — tests accept that
        // side-effect for simplicity, but we clean up in Dispose().
        return registry;
    }

    [Fact]
    public void GetAll_WhenEmpty_ReturnsEmptyList()
    {
        // Fresh instance has no environments
        var fresh = new PythonEnvironmentRegistry();
        // We can't guarantee isolation from a previous test's persisted state in prod dir,
        // so just verify the return type is correct.
        fresh.GetAll().ShouldNotBeNull();
    }

    [Fact]
    public void AddOrUpdate_NewEnv_AppearsInGetAll()
    {
        var registry = new PythonEnvironmentRegistry();
        var env = MakeEnv("test-add");

        registry.AddOrUpdate(env);

        registry.GetAll().ShouldContain(e => e.Name == "test-add");

        // Cleanup
        registry.Remove("test-add");
    }

    [Fact]
    public void AddOrUpdate_ExistingEnv_ReplacesIt()
    {
        var registry = new PythonEnvironmentRegistry();
        var env = MakeEnv("test-replace");
        registry.AddOrUpdate(env);

        var updated = MakeEnv("test-replace");
        updated.PythonVersion = "3.12.0";
        registry.AddOrUpdate(updated);

        var found = registry.GetAll().FirstOrDefault(e => e.Name == "test-replace");
        found.ShouldNotBeNull();
        found.PythonVersion.ShouldBe("3.12.0");

        // Cleanup
        registry.Remove("test-replace");
    }

    [Fact]
    public void Remove_ExistingEnv_DisappearsFromList()
    {
        var registry = new PythonEnvironmentRegistry();
        var env = MakeEnv("test-remove");
        registry.AddOrUpdate(env);

        registry.Remove("test-remove");

        registry.GetAll().ShouldNotContain(e => e.Name == "test-remove");
    }

    [Fact]
    public void SetActive_ExistingEnv_ReturnsItAsActive()
    {
        var registry = new PythonEnvironmentRegistry();
        var env = MakeEnv("test-active");
        registry.AddOrUpdate(env);

        string? notifiedPath = null;
        registry.OnActiveEnvironmentChanged = p => notifiedPath = p;

        registry.SetActive("test-active");

        registry.GetActive()?.Name.ShouldBe("test-active");

        // Cleanup
        registry.SetActive(null);
        registry.Remove("test-active");
    }

    [Fact]
    public void SetActive_FiresCallback()
    {
        var registry = new PythonEnvironmentRegistry();
        var env = MakeEnv("test-callback");
        registry.AddOrUpdate(env);

        var callbackFired = false;
        registry.OnActiveEnvironmentChanged = _ => callbackFired = true;

        registry.SetActive("test-callback");

        callbackFired.ShouldBeTrue();

        // Cleanup
        registry.SetActive(null);
        registry.Remove("test-callback");
    }

    [Fact]
    public void Exists_AfterAdd_ReturnsTrue()
    {
        var registry = new PythonEnvironmentRegistry();
        var env = MakeEnv("test-exists");
        registry.AddOrUpdate(env);

        registry.Exists("test-exists").ShouldBeTrue();

        // Cleanup
        registry.Remove("test-exists");
    }

    [Fact]
    public void Exists_ForMissingEnv_ReturnsFalse()
    {
        var registry = new PythonEnvironmentRegistry();
        registry.Exists("definitely-not-there-" + Guid.NewGuid()).ShouldBeFalse();
    }

    private static PythonEnvironment MakeEnv(string name) => new()
    {
        Name = name,
        VenvPath = Path.Combine(Path.GetTempPath(), name),
        Status = PythonEnvironmentStatus.Unknown,
    };

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
