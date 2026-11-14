using System.Diagnostics;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.CodeExporter;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration.LengthLock;

/// <summary>
/// The length-lock journey on the shipped fibre-array example, across every seam the lock group
/// (routes sharing one rounded target length) must survive: equalize all 16 channels, save and
/// reopen, Bend → Auto, a far move with Re-equalize, the Nazca export, and a second reopen. If any
/// seam dropped or drifted a target, the group would silently split while each piece's own tests
/// stayed green.
/// </summary>
public class LengthLockGroupJourneyTests : IDisposable
{
    private const int ChannelCount = FiberArrayExampleBuilder.ChannelCount;
    private const double MinimumShippedSpreadMicrometers = 200;
    private const double EqualizeToleranceMicrometers = 0.1;
    private const double ReopenLengthSlackMicrometers = 0.01;
    private const double PositionSlackMicrometers = 0.01;

    /// <summary>
    /// Pushes the bottom channel's on-chip end this far right: the channel is the outermost one,
    /// so it stays clear of the bundle, but its shortest route grows well past the locked length.
    /// </summary>
    private const double FarMoveMicrometers = 500;

    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"lunima-length-lock-journey-{Guid.NewGuid():N}");

    /// <summary>Creates the test with its own temp directory for the saved designs.</summary>
    public LengthLockGroupJourneyTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>Removes the saved designs.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    /// <summary>The journey without the Bend → Auto step: every other seam keeps the group of 16.</summary>
    [Fact]
    public Task ShippedFanIn_StaysOneLockGroupOf16_ThroughReopen_MoveReEqualize_Export_AndReopen() =>
        RunJourneyAsync(withBendThenAuto: false);

    /// <summary>
    /// The whole journey as specified, Bend → Auto included: the arches cannot reach the lock, so
    /// Auto routes the bundle and meanders it back onto the lock.
    /// </summary>
    [Fact]
    public Task ShippedFanIn_BendThenAuto_KeepsAll16OnTheLock_ThroughTheWholeJourney() =>
        RunJourneyAsync(withBendThenAuto: true);

    private async Task RunJourneyAsync(bool withBendThenAuto)
    {
        var clock = Stopwatch.StartNew();
        var shipped = await OpenShippedExampleAsync();
        double target = await EqualizeAllAsync(shipped);
        var equalized = shipped.CaptureLocks();

        var reopened = await shipped.SaveAndReopenAsync(TempFile("equalized.lun"));
        AssertSameLocks(reopened, equalized, "after save/reopen");

        if (withBendThenAuto)
            await BendThenAutoAsync(reopened, target);
        double reEqualizedTarget = await MoveOneEndAndReEqualizeAsync(reopened, target);
        var reEqualized = reopened.CaptureLocks();
        AssertExportKeepsTheLengths(reopened, reEqualizedTarget);

        var reopenedAgain = await reopened.SaveAndReopenAsync(TempFile("re-equalized.lun"));
        AssertSameLocks(reopenedAgain, reEqualized, "after the second save/reopen");
        _output.WriteLine($"Lock {target:F2} µm, re-equalized {reEqualizedTarget:F2} µm; "
            + $"journey wall time {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Step 1: the shipped file opens with every route found and the lengths clearly unequal.</summary>
    private static async Task<LengthLockJourneySession> OpenShippedExampleAsync()
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), FiberArrayExampleBuilder.ExampleFileName);
        var session = await LengthLockJourneySession.OpenAsync(path);
        var connections = session.Connections;
        connections.Count.ShouldBe(ChannelCount);
        AssertAllRouted(connections, "after opening the shipped example");
        var lengths = connections.Select(c => c.Connection.PathLengthMicrometers).ToList();
        (lengths.Max() - lengths.Min()).ShouldBeGreaterThan(MinimumShippedSpreadMicrometers);
        return session;
    }

    /// <summary>Step 2: select all, "Equalize lengths" — one lock group of 16 at one common length.</summary>
    private static async Task<double> EqualizeAllAsync(LengthLockJourneySession session)
    {
        session.SelectAll();
        await session.Equalize.EqualizeCommand.ExecuteAsync(null);
        session.Equalize.HasErrorStatus.ShouldBeFalse(session.Equalize.StatusMessage);
        return AssertOneLockGroup(session, "after equalizing");
    }

    /// <summary>Step 4: Bend → Auto on the reopened selection keeps every route at the locked length.</summary>
    private static async Task BendThenAutoAsync(LengthLockJourneySession session, double target)
    {
        session.SelectAll();
        await session.RestyleSelectionAsync(WaveguideType.Bend);
        await session.RestyleSelectionAsync(WaveguideType.Auto);
        AssertOneLockGroup(session, "after Bend → Auto").ShouldBe(target, "Bend → Auto must keep the lock's length");
    }

    /// <summary>
    /// Step 5: a far move makes exactly one lock unreachable, Design Validation names that route,
    /// and "Re-equalize group" relocks all 16 at one new common length.
    /// </summary>
    private static async Task<double> MoveOneEndAndReEqualizeAsync(LengthLockJourneySession session, double target)
    {
        var moved = session.Connections.MaxBy(c => c.Connection.StartPin.GetAbsolutePosition().y)!;
        var end = session.Canvas.Components.Single(c => c.Component == moved.Connection.EndPin.ParentComponent);
        session.Canvas.MoveComponent(end, FarMoveMicrometers, 0);
        await FiberArrayExampleBuilder.WaitForRoutingAsync(session.Canvas);

        LengthLockActions.IsViolated(moved).ShouldBeTrue($"{moved.Connection.PathLengthMicrometers:F2} µm cannot hold {target:F2} µm");
        session.Connections.Where(c => c != moved).ShouldAllBe(c => !LengthLockActions.IsViolated(c));
        session.LengthTargetIssues().ShouldHaveSingleItem().Connection.ShouldBeSameAs(moved.Connection);

        await session.Equalize.Locks.ReEqualizeAsync(moved);

        session.Equalize.HasErrorStatus.ShouldBeFalse(session.Equalize.StatusMessage);
        double reEqualized = AssertOneLockGroup(session, "after Re-equalize group");
        reEqualized.ShouldBeGreaterThan(target, "the moved route can no longer be as short as the old lock");
        return reEqualized;
    }

    /// <summary>Step 6: the Nazca script carries every route at its canvas length, i.e. at the lock.</summary>
    private static void AssertExportKeepsTheLengths(LengthLockJourneySession session, double target)
    {
        var connections = session.Connections.Select(c => c.Connection).ToList();
        var script = new SimpleNazcaExporter().Export(session.Canvas);
        var exported = ExportedRouteLengths.Read(script, connections);
        for (int i = 0; i < connections.Count; i++)
            AssertExportedRoute(connections[i], exported[i], target);

        var validation = new ExportValidator().Validate(
            session.Canvas.Components.Select(c => c.Component).ToList(), connections, script);
        validation.Errors.ShouldBeEmpty("the export validator must find nothing wrong");
    }

    /// <summary>The exported route starts at its pin and is as long as on the canvas, i.e. on the lock.</summary>
    private static void AssertExportedRoute(WaveguideConnection connection, ExportedRoute exported, double target)
    {
        string name = LengthLockJourneySession.KeyOf(connection);
        var (pinX, pinY) = connection.StartPin.GetAbsolutePosition();
        var (nazcaX, nazcaY) = NazcaCoordinateMapper.ToNazca(pinX, pinY);
        exported.StartX.ShouldBe(nazcaX, PositionSlackMicrometers, $"{name}: exported route must start at its pin");
        exported.StartY.ShouldBe(nazcaY, PositionSlackMicrometers, $"{name}: exported route must start at its pin");

        double slack = ExportedRouteLengths.RoundingSlack(connection);
        exported.LengthMicrometers.ShouldBe(connection.PathLengthMicrometers, slack, $"{name}: exported vs canvas length");
        exported.LengthMicrometers.ShouldBe(target, connection.LengthToleranceMicrometers!.Value + slack,
            $"{name}: exported length vs the lock");
    }

    /// <summary>
    /// Steps 3 and 7: the reopened design carries the same lock on every connection, at the same
    /// length, and Design Validation finds no route missing its target.
    /// </summary>
    private static void AssertSameLocks(LengthLockJourneySession session,
        IReadOnlyDictionary<string, LockState> expected, string step)
    {
        var actual = session.CaptureLocks();
        actual.Keys.ShouldBe(expected.Keys, ignoreOrder: true, $"{step}: the same connections");
        foreach (var (key, before) in expected)
        {
            var after = actual[key];
            after.TargetMicrometers.ShouldBe(before.TargetMicrometers, $"{step}: {key} target");
            after.ToleranceMicrometers.ShouldBe(before.ToleranceMicrometers, $"{step}: {key} tolerance");
            after.LengthMicrometers.ShouldBe(before.LengthMicrometers, ReopenLengthSlackMicrometers, $"{step}: {key} length");
            after.GroupSize.ShouldBe(ChannelCount, $"{step}: {key} lock group");
        }
        session.Connections.ShouldAllBe(c => !LengthLockActions.IsViolated(c), $"{step}: no lock may be violated");
        session.LengthTargetIssues().ShouldBeEmpty($"{step}: Design Validation");
    }

    /// <summary>
    /// Every connection is routed, locked to one common target, within the equalize tolerance of it,
    /// and in a lock group of all 16; returns the common target.
    /// </summary>
    private static double AssertOneLockGroup(LengthLockJourneySession session, string step)
    {
        var connections = session.Connections;
        AssertAllRouted(connections, step);
        var targets = connections.Select(c => c.Connection.TargetLengthMicrometers).Distinct().ToList();
        double target = targets.ShouldHaveSingleItem($"{step}: one common target").ShouldNotBeNull($"{step}: locked");
        foreach (var connection in connections)
        {
            string name = LengthLockJourneySession.KeyOf(connection.Connection);
            connection.Connection.PathLengthMicrometers.ShouldBe(target, EqualizeToleranceMicrometers, $"{step}: {name} length");
            session.Equalize.Locks.GroupOf(connection).Count.ShouldBe(ChannelCount, $"{step}: {name} lock group");
            LengthLockActions.IsViolated(connection).ShouldBeFalse($"{step}: {name} lock");
        }
        return target;
    }

    private static void AssertAllRouted(IReadOnlyList<WaveguideConnectionViewModel> connections, string step) =>
        connections.ShouldAllBe(c => c.Connection.RoutedPath != null && !c.Connection.IsBlockedFallback,
            $"{step}: every route found, none blocked");

    private string TempFile(string name) => Path.Combine(_tempDirectory, name);
}
