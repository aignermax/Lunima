using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Moq;
using Shouldly;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for NewProject workflow across ViewModels.
/// Tests the complete flow from MainViewModel through FileOperationsViewModel.
/// </summary>
public class NewProjectIntegrationTests
{
    private readonly SimulationService _simulationService;
    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly PdkLoader _pdkLoader;
    private readonly CommandManager _commandManager;
    private readonly UserPreferencesService _preferencesService;
    private readonly GroupLibraryManager _groupLibraryManager;
    private readonly CAP.Avalonia.Services.GroupPreviewGenerator _previewGenerator;
    private readonly IInputDialogService _inputDialogService;
    private readonly CAP_Core.Export.GdsExportService _gdsExportService;

    public NewProjectIntegrationTests()
    {
        _simulationService = new SimulationService();
        _nazcaExporter = new SimpleNazcaExporter();
        _pdkLoader = new PdkLoader();
        _commandManager = new CommandManager();
        _preferencesService = new UserPreferencesService();
        _groupLibraryManager = new GroupLibraryManager();
        _previewGenerator = new CAP.Avalonia.Services.GroupPreviewGenerator();
        _inputDialogService = new InputDialogService();
        _gdsExportService = new CAP_Core.Export.GdsExportService();
    }

    [Fact]
    public async Task MainViewModel_NewProject_ClearsCanvas()
    {
        var mainVm = new MainViewModel(
            _simulationService,
            _nazcaExporter,
            _pdkLoader,
            _commandManager,
            _preferencesService,
            _groupLibraryManager,
            _previewGenerator,
            _inputDialogService,
            _gdsExportService);

        // Add a component to the canvas
        var component = TestComponentFactory.CreateStraightWaveGuide();
        mainVm.Canvas.AddComponent(component, "TestTemplate");

        // Mark as saved
        mainVm.FileOperations.HasUnsavedChanges = false;

        // Execute NewProject
        await mainVm.NewProjectCommand.ExecuteAsync(null);

        // Verify canvas is cleared
        mainVm.Canvas.Components.Count.ShouldBe(0);
        mainVm.Canvas.Connections.Count.ShouldBe(0);
        mainVm.FileOperations.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task MainViewModel_NewProject_PromptsWhenUnsaved()
    {
        var mainVm = new MainViewModel(
            _simulationService,
            _nazcaExporter,
            _pdkLoader,
            _commandManager,
            _preferencesService,
            _groupLibraryManager,
            _previewGenerator,
            _inputDialogService,
            _gdsExportService);

        var mockMessageBox = new Mock<IMessageBoxService>();
        mockMessageBox
            .Setup(m => m.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.DontSave);

        mainVm.FileOperations.MessageBoxService = mockMessageBox.Object;

        // Add component and ensure it's marked as unsaved
        var component = TestComponentFactory.CreateStraightWaveGuide();
        mainVm.Canvas.AddComponent(component, "TestTemplate");
        mainVm.FileOperations.HasUnsavedChanges.ShouldBeTrue();

        // Execute NewProject
        await mainVm.NewProjectCommand.ExecuteAsync(null);

        // Verify dialog was shown
        mockMessageBox.Verify(
            m => m.ShowSavePromptAsync(
                It.Is<string>(s => s.Contains("save")),
                It.IsAny<string>()),
            Times.Once);

        // Canvas should be cleared
        mainVm.Canvas.Components.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NewProject_PreservesCommandHistory_AfterClear()
    {
        var mainVm = new MainViewModel(
            _simulationService,
            _nazcaExporter,
            _pdkLoader,
            _commandManager,
            _preferencesService,
            _groupLibraryManager,
            _previewGenerator,
            _inputDialogService,
            _gdsExportService);

        // Add component
        var component = TestComponentFactory.CreateStraightWaveGuide();
        mainVm.Canvas.AddComponent(component, "TestTemplate");

        // Mark as saved
        mainVm.FileOperations.HasUnsavedChanges = false;

        // Execute NewProject
        await mainVm.NewProjectCommand.ExecuteAsync(null);

        // Command history should be cleared
        mainVm.CommandManager.CanUndo.ShouldBeFalse();
        mainVm.CommandManager.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public async Task NewProject_ClearsConnections()
    {
        var mainVm = new MainViewModel(
            _simulationService,
            _nazcaExporter,
            _pdkLoader,
            _commandManager,
            _preferencesService,
            _groupLibraryManager,
            _previewGenerator,
            _inputDialogService,
            _gdsExportService);

        // Add two components with physical pins
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = mainVm.Canvas.AddComponent(comp1, "Template1");
        var vm2 = mainVm.Canvas.AddComponent(comp2, "Template2");

        vm1.X = 0;
        vm1.Y = 0;
        vm2.X = 200;
        vm2.Y = 0;

        // Try to connect the components
        if (comp1.PhysicalPins.Count > 0 && comp2.PhysicalPins.Count > 0)
        {
            mainVm.Canvas.ConnectPins(comp1.PhysicalPins[0], comp2.PhysicalPins[0]);
        }

        // Mark as saved
        mainVm.FileOperations.HasUnsavedChanges = false;

        // Execute NewProject
        await mainVm.NewProjectCommand.ExecuteAsync(null);

        // Verify connections are cleared
        mainVm.Canvas.Connections.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NewProject_ExitsGroupEditMode()
    {
        var mainVm = new MainViewModel(
            _simulationService,
            _nazcaExporter,
            _pdkLoader,
            _commandManager,
            _preferencesService,
            _groupLibraryManager,
            _previewGenerator,
            _inputDialogService,
            _gdsExportService);

        // Create a group with some components
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        mainVm.Canvas.AddComponent(group);

        // Enter group edit mode
        mainVm.Canvas.EnterGroupEditMode(group);
        mainVm.Canvas.IsInGroupEditMode.ShouldBeTrue();

        // Mark as saved
        mainVm.FileOperations.HasUnsavedChanges = false;

        // Execute NewProject
        await mainVm.NewProjectCommand.ExecuteAsync(null);

        // Verify group edit mode is exited
        mainVm.Canvas.IsInGroupEditMode.ShouldBeFalse();
        mainVm.Canvas.CurrentEditGroup.ShouldBeNull();

        // Verify canvas is cleared
        mainVm.Canvas.Components.Count.ShouldBe(0);
        mainVm.Canvas.Connections.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NewProject_ExitsNestedGroupEditMode()
    {
        var mainVm = new MainViewModel(
            _simulationService,
            _nazcaExporter,
            _pdkLoader,
            _commandManager,
            _preferencesService,
            _groupLibraryManager,
            _previewGenerator,
            _inputDialogService,
            _gdsExportService);

        // Create nested groups
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var childGroup = new ComponentGroup("ChildGroup");
        childGroup.AddChild(comp);

        var parentGroup = new ComponentGroup("ParentGroup");
        parentGroup.AddChild(childGroup);

        mainVm.Canvas.AddComponent(parentGroup);

        // Enter nested group edit mode
        mainVm.Canvas.EnterGroupEditMode(parentGroup);
        mainVm.Canvas.EnterGroupEditMode(childGroup);
        mainVm.Canvas.IsInGroupEditMode.ShouldBeTrue();

        // Mark as saved
        mainVm.FileOperations.HasUnsavedChanges = false;

        // Execute NewProject
        await mainVm.NewProjectCommand.ExecuteAsync(null);

        // Verify all group edit modes are exited
        mainVm.Canvas.IsInGroupEditMode.ShouldBeFalse();
        mainVm.Canvas.CurrentEditGroup.ShouldBeNull();
        mainVm.Canvas.BreadcrumbPath.Count.ShouldBe(0);

        // Verify canvas is cleared
        mainVm.Canvas.Components.Count.ShouldBe(0);
    }
}
