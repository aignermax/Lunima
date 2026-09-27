using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Diagnostics;
using CAP.Avalonia.ViewModels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// UI-responsiveness budget harness (issue #1150, budget from CLAUDE.md §5): every
/// user-facing <c>[RelayCommand]</c> on <see cref="MainViewModel"/> must keep its
/// UI-thread-blocking prefix under 100 ms against the largest shipped logic example
/// (<c>examples/Logic Gate 4-Bit Adder.lun</c>). <see cref="UiThreadWatchdog"/>
/// measures each invocation; a stall surfaces the command name and duration in the
/// failure message so the offender can be backgrounded or explicitly allow-listed
/// with a follow-up issue.
///
/// The harness deliberately covers only commands that (a) require no dialog, file
/// picker, or unsaved-changes prompt, (b) accept no parameter, and (c) are reachable
/// from the loaded-example state with <c>CanExecute == true</c>. Anything outside
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
            // File pickers / save prompts — no headless dialog service, and the heavy
            // work already happens off-thread after the picker returns.
            ["SaveDesignCommand"] = "opens a save-file picker; save I/O is already async",
            ["SaveDesignAsCommand"] = "opens a save-file picker; save I/O is already async",
            ["LoadDesignCommand"] = "opens an open-file picker; load I/O is already async",
            ["NewProjectCommand"] = "prompts to save unsaved changes",
            ["ExportNazcaCommand"] = "opens a save-file picker",
            ["ExportSaxCommand"] = "opens a save-file picker",
            ["LoadPdkCommand"] = "opens an open-file picker",
            // Window / tab openers — trivially fast, but they launch windows the
            // headless session cannot close cleanly.
            ["OpenSettingsWindowCommand"] = "opens the Settings window",
            ["OpenAiSettingsCommand"] = "opens the Settings window on the AI page",
            ["OpenPdkOffsetEditorCommand"] = "opens the PDK offset editor window",
            ["OpenPdkHelpCommand"] = "launches the browser via IUrlLauncher",
            // Requires external state this fixture does not set up.
            ["PasteSelectedCommandCommand"] = "needs a populated clipboard",
            // Selection-dependent: exercised separately with a real selection so the
            // harness can also time the routed-path recompute they trigger.
            ["DeleteSelectedCommand"] = "needs an active selection — covered by the selection variant",
            ["CopySelectedCommand"] = "needs an active selection — covered by the selection variant",
            ["RotateSelectedCommand"] = "needs an active selection — covered by the selection variant",
            ["CreateGroupCommand"] = "needs an active selection — covered by the selection variant",
            ["UngroupCommand"] = "needs an active selection — covered by the selection variant",
        };

    /// <summary>
    /// Commands the harness drives with no extra setup beyond the loaded example.
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
        "RunDesignChecksCommand",
        "ShowHomeCommand",
    };

    /// <summary>
    /// Runs every <see cref="TimedCommands"/> entry against the loaded 4-bit adder and
    /// asserts no UI-thread stall exceeds <see cref="UiThreadWatchdog.BudgetMs"/>.
    /// </summary>
    [AvaloniaFact]
    public async Task RelayCommands_OnLoadedFourBitAdder_StayUnderResponsivenessBudget()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        (await vm.FileOperations.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue(
            $"'{ExampleFileName}' must load through the real load path");

        var stalls = new List<UiStallMeasurement>();
        void OnStall(UiStallMeasurement m) => stalls.Add(m);
        UiThreadWatchdog.StallDetected += OnStall;
        try
        {
            foreach (var commandName in TimedCommands)
            {
                var command = GetCommand(vm, commandName);
                if (!command.CanExecute(null))
                    continue; // CanExecute gates are a UX concern, not a responsiveness one.

                if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
                {
                    await UiThreadWatchdog.MeasureAsync(commandName, () => asyncCommand.ExecuteAsync(null));
                }
                else
                {
                    UiThreadWatchdog.Measure(commandName, () => command.Execute(null));
                }
            }
        }
        finally
        {
            UiThreadWatchdog.StallDetected -= OnStall;
        }

        stalls.ShouldBeEmpty(
            "UI-thread stalls over the 100 ms budget — background these handlers " +
            "(Task.Run + busy state) or allow-list them with a follow-up issue link:\n  " +
            string.Join("\n  ", stalls.Select(s => s.ToString())));
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
