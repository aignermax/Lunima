using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Workspaces.ContextPanels;

/// <summary>
/// The left rail's Gates panel (Logic workspace default): the library's saved groups that
/// are gates (carry a truth-table pin assignment, directly or in a nested group) as
/// drag-in / click-to-place templates, the shipped starter gate examples as "open example"
/// entries, and "Make gate from selection". The raw component library stays one click away.
/// </summary>
public partial class GatesPanelViewModel : ObservableObject
{
    /// <summary>File names of the shipped starter gate examples, in ladder order.</summary>
    public static readonly IReadOnlyList<string> GateExampleFiles =
    [
        "Logic Gate - Build your first gate.lun",
        "Logic Gate NOT-NAND.lun",
        "Logic Gate AND-from-NAND.lun",
        "Logic Gate OR-AND.lun",
    ];

    private readonly ComponentLibraryViewModel _library;
    private readonly ObservableCollection<ExampleDesign> _examples;

    /// <summary>Saved groups (user and PDK macros) that are logic gates.</summary>
    public ObservableCollection<GroupTemplateItemViewModel> SavedGates { get; } = new();

    /// <summary>The shipped starter gate examples that are installed.</summary>
    public ObservableCollection<ExampleDesign> GateExamples { get; } = new();

    /// <summary>True when at least one saved group is a gate.</summary>
    [ObservableProperty]
    private bool _hasSavedGates;

    /// <summary>True when any starter gate example is installed.</summary>
    [ObservableProperty]
    private bool _hasGateExamples;

    /// <summary>Opens an example design as an untitled copy; returns false when cancelled.</summary>
    public Func<string, Task<bool>>? OpenExampleRequested { get; set; }

    /// <summary>Runs "Make gate…" on the current canvas selection.</summary>
    public Action? MakeGateRequested { get; set; }

    /// <summary>Switches the rail to the full component library.</summary>
    public Action? ShowLibraryRequested { get; set; }

    /// <summary>Creates the panel over the group library and the shipped examples list.</summary>
    /// <param name="library">Saved groups and PDK macros.</param>
    /// <param name="examples">The shipped examples (Home screen list).</param>
    public GatesPanelViewModel(ComponentLibraryViewModel library, ObservableCollection<ExampleDesign> examples)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _examples = examples ?? throw new ArgumentNullException(nameof(examples));
        _library.UserGroups.CollectionChanged += OnLibraryChanged;
        _library.PdkGroups.CollectionChanged += OnLibraryChanged;
        _examples.CollectionChanged += (_, _) => RefreshExamples();
        RefreshSavedGates();
        RefreshExamples();
    }

    /// <summary>
    /// True when <paramref name="template"/> is a gate: its group, or a group nested in it,
    /// carries a truth-table pin assignment.
    /// </summary>
    public static bool IsGate(GroupTemplate template) =>
        template.TemplateGroup is { } group
        && WorkspaceDefaults.Flatten([group]).OfType<ComponentGroup>()
            .Any(g => g.TruthTablePinAssignment != null);

    /// <summary>Opens <paramref name="example"/> as an untitled copy.</summary>
    [RelayCommand]
    private async Task OpenExample(ExampleDesign? example)
    {
        if (example == null || OpenExampleRequested == null)
            return;
        await OpenExampleRequested(example.FilePath);
    }

    /// <summary>"Make gate from selection".</summary>
    [RelayCommand]
    private void MakeGate() => MakeGateRequested?.Invoke();

    /// <summary>Opens the full component library.</summary>
    [RelayCommand]
    private void ShowLibrary() => ShowLibraryRequested?.Invoke();

    private void OnLibraryChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSavedGates();

    private void RefreshSavedGates()
    {
        SavedGates.Clear();
        foreach (var item in _library.UserGroups.Concat(_library.PdkGroups).Where(i => IsGate(i.Template)))
            SavedGates.Add(item);
        HasSavedGates = SavedGates.Count > 0;
    }

    private void RefreshExamples()
    {
        GateExamples.Clear();
        foreach (var file in GateExampleFiles)
        {
            var example = _examples.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FilePath), file, StringComparison.OrdinalIgnoreCase));
            if (example != null)
                GateExamples.Add(example);
        }
        HasGateExamples = GateExamples.Count > 0;
    }
}
