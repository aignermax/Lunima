using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Diagnostics;
using CAP.Avalonia.ViewModels;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// UI-responsiveness budget harness (issue #1150, budget from CLAUDE.md §5): every
/// user-facing <c>[RelayCommand]</c> on <see cref="MainViewModel"/> must keep its
/// UI-thread stalls under 100 ms throughout the complete operation against the largest shipped logic example
/// (<c>examples/Logic Gate 4-Bit Adder.lun</c>). <see cref="UiThreadWatchdog"/>
/// measures each invocation; a stall surfaces the command name and duration in the
/// failure message so the blocking work can be corrected.
///
/// The harness deliberately covers only commands that (a) require no dialog, file
/// picker, or unsaved-changes prompt, (b) accept no parameter, and (c) have explicit
/// preconditions on a freshly loaded example. Anything outside
/// that envelope is excluded by the <see cref="SkippedCommands"/> table with a reason.
/// </summary>
[Collection("LocalizationSingleton")]
public class ResponsivenessBudgetTests
{
    private const string ExampleFileName = "Logic Gate 4-Bit Adder.lun";

    /// <summary>
    /// Commands intentionally outside this harness, each with the reason. An entry
    /// here is an explicit, reviewed decision — not silent coverage loss. New
    /// [RelayCommand]s on MainViewModel are picked up by reflection and will fail
    /// the test with "uncovered command" until they either join the timed set or
    /// are added here with a justification.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SkippedCommands =
        new Dictionary<string, string>
        {
            // Dialog-driven workflows require a scripted interaction scenario.
            ["SaveDesignCommand"] = "requires a scripted save-file picker; not measured here",
            ["SaveDesignAsCommand"] = "requires a scripted save-file picker; not measured here",
            ["LoadDesignCommand"] = "requires a scripted open-file picker; not measured here",
            ["NewProjectCommand"] = "prompts to save unsaved changes",
            ["ExportNazcaCommand"] = "opens a save-file picker",
            ["ExportSaxCommand"] = "opens a save-file picker",
            ["LoadPdkCommand"] = "opens an open-file picker",
            ["NewDieQuickCommand"] = "requires a configured new-die/process workflow; not measured here",
            // Window and browser workflows require a separate host.
            ["OpenSettingsWindowCommand"] = "opens the Settings window",
            ["OpenAiSettingsCommand"] = "opens the Settings window on the AI page",
            ["OpenPreviewPythonSettingsCommand"] = "opens the Settings window on the Python environments page; not measured here",
            ["OpenPdkOffsetEditorCommand"] = "opens the PDK offset editor window",
            ["OpenPdkHelpCommand"] = "launches the browser via IUrlLauncher",
            // Requires external state this fixture does not set up.
            ["PasteSelectedCommandCommand"] = "needs a populated clipboard",
            // These selection/clipboard workflows are not measured by this harness.
            ["DeleteSelectedCommand"] = "requires a separate selection/mutation scenario; not measured here",
            ["CopySelectedCommand"] = "requires a separate selection/mutation scenario; not measured here",
            ["RotateSelectedCommand"] = "requires a separate selection/mutation scenario; not measured here",
            ["CreateGroupCommand"] = "requires a separate selection/mutation scenario; not measured here",
            ["UngroupCommand"] = "requires a separate selection/mutation scenario; not measured here",
        };

    /// <summary>
    /// Commands measured after the loaded fixture establishes explicit preconditions.
    /// Keyed by the generated RelayCommand property name so reflection can validate
    /// the list against MainViewModel — a renamed or removed command fails the test.
    /// </summary>
    private static readonly string[] TimedCommands =
    {
        "SetSelectModeCommand",
        "SetConnectModeCommand",
        "SetDeleteModeCommand",
        "SetProbeModeCommand",
        "SetCutModeCommand",
        "ZoomInCommand",
        "ZoomOutCommand",
        "ResetZoomCommand",
        "ResetPanCommand",
        "UndoCommand",
        "RedoCommand",
        "RunSimulationCommand",
        "RunWorkspaceCommand",
        "RunWorkspaceFromShortcutCommand",
        "TogglePowerOverlayCommand",
        "RunDesignChecksCommand",
        "ShowHomeCommand",
        "MakeGateCommand",
        "RefreshSystemOverviewCommand",
        "SwitchToLayoutCommand",
        "ShowInLayoutCommand",
        "SelectAllCommand",
    };

    public static IEnumerable<object[]> CommandCases => TimedCommands.Select(name => new object[] { name });

    /// <summary>Warms an independent fixture, then measures one fresh equivalent fixture without retries.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(CommandCases))]
    public async Task RelayCommands_OnLoadedFourBitAdder_StayUnderResponsivenessBudget(string commandName)
    {
        string warmedShape;
        await using (var warmup = await ResponsivenessFixture.LoadAsync(ExampleFileName, commandName))
        {
            warmedShape = warmup.Shape;
            await ExecuteAsync(warmup.Vm, commandName);
            warmup.AssertHistoryEffect(commandName);
        }

        await using var measured = await ResponsivenessFixture.LoadAsync(ExampleFileName, commandName);
        measured.Shape.ShouldBe(warmedShape, "warm-up and measurement must start from equivalent state");
        var stalls = new List<UiStallMeasurement>();
        void Capture(UiStallMeasurement stall) => stalls.Add(stall);
        UiThreadWatchdog.StallDetected += Capture;
        try
        {
            // Even synchronous commands run inside the whole-operation heartbeat window.
            await UiThreadWatchdog.MeasureAsync(commandName, () => ExecuteAsync(measured.Vm, commandName));
        }
        finally
        {
            UiThreadWatchdog.StallDetected -= Capture;
        }
        measured.AssertHistoryEffect(commandName);
        stalls.ShouldBeEmpty("UI-thread stalls exceeded the unchanged 100 ms budget: "
            + string.Join("; ", stalls));
    }

    private static async Task ExecuteAsync(MainViewModel vm, string commandName)
    {
        var command = GetCommand(vm, commandName);
        command.CanExecute(null).ShouldBeTrue(commandName + " must execute; fixture setup may not silently skip it");
        if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
            await asyncCommand.ExecuteAsync(null);
        else
            command.Execute(null);
    }

    /// <summary>
    /// Guards the harness against silent coverage drift: every <c>[RelayCommand]</c>
    /// discovered on <see cref="MainViewModel"/> must appear either in
    /// <see cref="TimedCommands"/> or in <see cref="SkippedCommands"/>. Adding a new
    /// command to MainViewModel fails this test until the author decides explicitly
    /// whether it joins the timed set or is excluded with a reason.
    /// </summary>
    [AvaloniaFact]
    public void EveryMainViewModelRelayCommand_IsEitherTimedOrExplicitlySkipped()
    {
        var timed = TimedCommands.ToHashSet(StringComparer.Ordinal);
        timed.Count.ShouldBe(TimedCommands.Length, "each timed command must have exactly one measured case");
        var discovered = DiscoverRelayCommandPropertyNames(typeof(MainViewModel)).ToHashSet();
        timed.Except(discovered).ShouldBeEmpty("timed command names must still exist");
        SkippedCommands.Keys.Except(discovered).ShouldBeEmpty("excluded command names must still exist");
        timed.Intersect(SkippedCommands.Keys).ShouldBeEmpty("a command cannot be both timed and excluded");
        var skipped = SkippedCommands.Keys;
        var uncovered = DiscoverRelayCommandPropertyNames(typeof(MainViewModel))
            .Where(name => !timed.Contains(name) && !skipped.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        uncovered.ShouldBeEmpty(
            "new RelayCommands on MainViewModel must be timed by the responsiveness " +
            "harness or added to SkippedCommands with a reason — uncovered: " +
            string.Join(", ", uncovered));
    }

    /// <summary>
    /// Resolves a generated <c>*Command</c> property on <paramref name="vm"/> to its
    /// <see cref="CommunityToolkit.Mvvm.Input.IRelayCommand"/>. Fails the test with a
    /// clear message when the harness list drifts from the ViewModel.
    /// </summary>
    private static CommunityToolkit.Mvvm.Input.IRelayCommand GetCommand(MainViewModel vm, string propertyName)
    {
        var property = typeof(MainViewModel).GetProperty(propertyName);
        property.ShouldNotBeNull($"MainViewModel must expose a '{propertyName}' RelayCommand property");
        var value = property.GetValue(vm);
        value.ShouldBeAssignableTo<CommunityToolkit.Mvvm.Input.IRelayCommand>(
            $"'{propertyName}' must be an IRelayCommand");
        return (CommunityToolkit.Mvvm.Input.IRelayCommand)value;
    }

    /// <summary>
    /// Reflects every property whose name ends in <c>Command</c> and whose type is
    /// assignable to <see cref="CommunityToolkit.Mvvm.Input.IRelayCommand"/> — the
    /// shape CommunityToolkit's source generator emits for <c>[RelayCommand]</c>.
    /// </summary>
    private static IEnumerable<string> DiscoverRelayCommandPropertyNames(Type viewModelType) =>
        viewModelType
            .GetProperties()
            .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal)
                     && typeof(CommunityToolkit.Mvvm.Input.IRelayCommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name);
}
