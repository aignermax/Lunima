using CAP.Avalonia.ViewModels;
using Shouldly;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Routing.LengthEqualization;

/// <summary>
/// The shipped fibre-array example end to end: built through the UI commands, all routes found,
/// lengths clearly unequal; selecting every connection and pressing "Equalize lengths" brings
/// them to the longest one's length in one undo step, within the interactive time budget.
/// Also authors the example file when <c>CAP_AUTHOR_EXAMPLES=1</c>.
/// </summary>
public class FiberArrayExampleTests
{
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";
    private const double Tolerance = 0.1;
    private const double MinimumSpreadMicrometers = 200;
    private static readonly TimeSpan InteractiveBudget = TimeSpan.FromSeconds(5);
    private readonly ITestOutputHelper _output;

    public FiberArrayExampleTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task SelectAll_EqualizeLengths_MatchesEveryChannel()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Canvas.CommandManager = vm.CommandManager; // as the DI composition wires it
        var connections = await FiberArrayExampleBuilder.BuildAsync(vm);
        connections.ShouldAllBe(c => c.Connection.RoutedPath != null && !c.Connection.IsBlockedFallback);
        var lengths = connections.Select(c => c.Connection.PathLengthMicrometers).ToList();
        (lengths.Max() - lengths.Min()).ShouldBeGreaterThan(MinimumSpreadMicrometers);

        foreach (var connection in connections)
            vm.Canvas.Selection.SelectedConnections.Add(connection);
        var panel = vm.BottomPanel.LengthEqualization;
        var started = DateTime.UtcNow;
        await panel.EqualizeCommand.ExecuteAsync(null);
        var elapsed = DateTime.UtcNow - started;
        _output.WriteLine($"{panel.StatusMessage} (wall {elapsed.TotalMilliseconds:F0} ms)");

        panel.HasErrorStatus.ShouldBeFalse(panel.StatusMessage);
        connections.ShouldAllBe(c => Math.Abs(c.Connection.PathLengthMicrometers - lengths.Max()) <= Tolerance);
        elapsed.ShouldBeLessThan(InteractiveBudget);
        vm.CommandManager.UndoDescription.ShouldStartWith("Equalize route lengths");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Author_FiberArrayExample()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        await FiberArrayExampleBuilder.BuildAsync(vm);
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), FiberArrayExampleBuilder.ExampleFileName);
        (await vm.FileOperations.SaveDesignToPathAsync(path)).ShouldBeTrue();
    }
}
