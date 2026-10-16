using CAP.Avalonia;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Settings;

/// <summary>
/// Builds the real production DI container (<see cref="App.ConfigureServices"/>) to verify
/// that the Routing settings page's two flags — adaptive crossing insertion and diagonal
/// routing — survive an app restart. Both were previously held only in memory, so a toggle
/// enabled by the user reverted to off the next time the app launched.
/// </summary>
public class RoutingSettingsPersistenceTests : IDisposable
{
    private readonly string _preferencesPath = Path.Combine(
        Path.GetTempPath(), $"lunima-routing-prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_preferencesPath))
            File.Delete(_preferencesPath);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossingInsertionEnabled_PersistedChoice_BinderHonorsIt(bool enabled)
    {
        new UserPreferencesService(_preferencesPath).SetCrossingInsertionEnabled(enabled);

        using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(_preferencesPath);
        var binder = sp.GetRequiredService<CrossingInsertionCanvasBinder>();

        binder.IsEnabled.ShouldBe(enabled);
    }

    [Fact]
    public void CrossingInsertionEnabled_ToggledOn_PersistsToPreferences()
    {
        new UserPreferencesService(_preferencesPath).SetCrossingInsertionEnabled(false);
        using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(_preferencesPath);
        var binder = sp.GetRequiredService<CrossingInsertionCanvasBinder>();
        binder.IsEnabled.ShouldBeFalse("an explicit disabled preference must be retained");

        binder.IsEnabled = true;

        new UserPreferencesService(_preferencesPath).GetCrossingInsertionEnabled().ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Startup_RestoresCrossingPolicy_WithoutStartingRouting(string? storedValue, bool expected)
    {
        File.WriteAllText(_preferencesPath, storedValue == null ? "{}"
            : "{\"CrossingInsertionEnabled\":" + storedValue + "}");
        using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(_preferencesPath);
        var canvas = sp.GetRequiredService<DesignCanvasViewModel>();
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 400;
        canvas.ConnectionManager.AddConnectionDeferred(
            start.PhysicalPins.First(p => p.Name == "out"), end.PhysicalPins.First(p => p.Name == "in"));
        int routingUpdates = 0;
        canvas.Routing.StateChanged += () => routingUpdates++;

        App.ActivateStartupSingletons(sp);

        (canvas.ConnectionManager.CrossingInsertion != null).ShouldBe(expected);
        routingUpdates.ShouldBe(0);
        canvas.IsRouting.ShouldBeFalse();
        canvas.ConnectionManager.Connections.Single().RoutedPath.ShouldBeNull();
    }

    /// <summary>
    /// Locks in the fix for the latent ordering dependency: restoring the persisted Crossings
    /// toggle previously relied entirely on <c>MainViewModel</c> listing the binder as an
    /// (optional) constructor parameter — a refactor removing that parameter would silently
    /// stop the restore (and crossing insertion itself) from ever running. Calling
    /// <see cref="App.ActivateStartupSingletons"/> — the explicit composition-root step that
    /// now owns this guarantee — must wire crossing insertion into the canvas without ever
    /// resolving <c>MainViewModel</c> or the binder type directly.
    /// </summary>
    [Fact]
    public void ActivateStartupSingletons_RestoresCrossingInsertion_WithoutResolvingMainViewModel()
    {
        new UserPreferencesService(_preferencesPath).SetCrossingInsertionEnabled(true);

        using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(_preferencesPath);
        App.ActivateStartupSingletons(sp);

        // The canvas' connection manager is the actual routing-time observable — checking it,
        // instead of resolving CrossingInsertionCanvasBinder again, proves the restore doesn't
        // depend on anything else in the graph referencing the binder.
        var canvas = sp.GetRequiredService<DesignCanvasViewModel>();
        canvas.ConnectionManager.CrossingInsertion.ShouldNotBeNull(
            "the persisted Crossings toggle must take effect before any ViewModel references the binder");
    }

    [Fact]
    public void UseDiagonalRouting_PersistedTrue_CanvasStartsEnabled()
    {
        new UserPreferencesService(_preferencesPath).SetUseDiagonalRouting(true);

        using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(_preferencesPath);
        var canvas = sp.GetRequiredService<DesignCanvasViewModel>();

        canvas.UseDiagonalRouting.ShouldBeTrue();
        canvas.Router.UseDiagonalRouting.ShouldBeTrue();
    }

    [Fact]
    public void UseDiagonalRouting_ToggledOn_PersistsToPreferences()
    {
        using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(_preferencesPath);
        var canvas = sp.GetRequiredService<DesignCanvasViewModel>();
        canvas.UseDiagonalRouting.ShouldBeFalse("diagonal routing is opt-in");

        canvas.UseDiagonalRouting = true;

        new UserPreferencesService(_preferencesPath).GetUseDiagonalRouting().ShouldBeTrue();
    }
}
