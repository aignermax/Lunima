using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using UnitTests.UI.Showcase;

namespace UnitTests.Integration.LengthLock;

/// <summary>A connection's length lock as the user would check it: target, tolerance, actual length, group size.</summary>
internal sealed record LockState(double? TargetMicrometers, double? ToleranceMicrometers, double LengthMicrometers, int GroupSize);

/// <summary>
/// One opened design in the real <see cref="MainViewModel"/> stack, driven the way the user does:
/// open a .lun file, select every connection, restyle the selection, save and reopen.
/// </summary>
internal sealed class LengthLockJourneySession
{
    private static readonly TimeSpan StylePatience = TimeSpan.FromSeconds(20);
    private const int StylePollMilliseconds = 20;

    private LengthLockJourneySession(MainViewModel viewModel) => ViewModel = viewModel;

    /// <summary>The application view model holding the opened design.</summary>
    public MainViewModel ViewModel { get; }

    /// <summary>The design canvas.</summary>
    public DesignCanvasViewModel Canvas => ViewModel.Canvas;

    /// <summary>The "Equalize lengths" section, owner of the padlock actions.</summary>
    public LengthEqualizationViewModel Equalize => ViewModel.BottomPanel.LengthEqualization;

    /// <summary>Every connection of the design, in canvas order.</summary>
    public IReadOnlyList<WaveguideConnectionViewModel> Connections => Canvas.Connections.ToList();

    /// <summary>Opens <paramref name="path"/> in a fresh view model and waits until its routes have settled.</summary>
    public static async Task<LengthLockJourneySession> OpenAsync(string path)
    {
        var viewModel = ShowcaseCircuit.CreateStagedViewModel();
        viewModel.Canvas.CommandManager = viewModel.CommandManager; // as the DI composition wires it
        (await viewModel.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue($"'{path}' must open");
        await viewModel.FileOperations.PostLoadRouting;
        await FiberArrayExampleBuilder.WaitForRoutingAsync(viewModel.Canvas);
        return new LengthLockJourneySession(viewModel);
    }

    /// <summary>Saves the design to <paramref name="path"/> and opens that file in a fresh view model.</summary>
    public async Task<LengthLockJourneySession> SaveAndReopenAsync(string path)
    {
        (await ViewModel.FileOperations.SaveDesignToPathAsync(path)).ShouldBeTrue($"saving to '{path}' must succeed");
        return await OpenAsync(path);
    }

    /// <summary>Puts every connection into the multi-selection, replacing what was selected.</summary>
    public void SelectAll()
    {
        Canvas.Selection.ClearConnectionSelection();
        foreach (var connection in Canvas.Connections)
            Canvas.Selection.SelectedConnections.Add(connection);
    }

    /// <summary>
    /// Picks <paramref name="style"/> in the routing-style dropdown for the current selection and
    /// waits until every selected connection carries it and the router has settled.
    /// </summary>
    public async Task RestyleSelectionAsync(WaveguideType style)
    {
        var selected = Canvas.Selection.SelectedConnections.ToList();
        ViewModel.BottomPanel.ConnectionRouting.SelectedStyle = style;
        var deadline = DateTime.UtcNow + StylePatience;
        while (DateTime.UtcNow < deadline && selected.Any(c => c.Connection.Type != style))
            await Task.Delay(StylePollMilliseconds);
        selected.ShouldAllBe(c => c.Connection.Type == style, $"the restyle to {style} must land within {StylePatience}");
        await FiberArrayExampleBuilder.WaitForRoutingAsync(Canvas);
    }

    /// <summary>The Design Validation issues saying a route misses its locked length.</summary>
    public IReadOnlyList<DesignIssue> LengthTargetIssues() =>
        new DesignValidator().Validate(Canvas.Connections.Select(c => c.Connection))
            .Where(issue => issue.Type == DesignIssueType.LengthTargetNotMet)
            .ToList();

    /// <summary>The lock of every connection, keyed by <see cref="KeyOf"/> so two sessions compare.</summary>
    public IReadOnlyDictionary<string, LockState> CaptureLocks() =>
        Canvas.Connections.ToDictionary(c => KeyOf(c.Connection), c => new LockState(
            c.Connection.TargetLengthMicrometers,
            c.Connection.LengthToleranceMicrometers,
            c.Connection.PathLengthMicrometers,
            Equalize.Locks.GroupOf(c).Count));

    /// <summary>A connection's identity that survives save/reopen: its two component identifiers and pin names.</summary>
    public static string KeyOf(WaveguideConnection connection) =>
        $"{connection.StartPin.ParentComponent.Identifier}.{connection.StartPin.Name}"
        + $" -> {connection.EndPin.ParentComponent.Identifier}.{connection.EndPin.Name}";
}
