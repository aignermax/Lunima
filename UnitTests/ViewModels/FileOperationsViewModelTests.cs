using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for FileOperationsViewModel.
/// Tests NewProject command, save prompt, and unsaved changes tracking.
/// </summary>
public class FileOperationsViewModelTests
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly ObservableCollection<ComponentTemplate> _componentLibrary;
    private readonly GdsExportViewModel _gdsExport;

    public FileOperationsViewModelTests()
    {
        _canvas = new DesignCanvasViewModel();
        _commandManager = new CommandManager();
        _nazcaExporter = new SimpleNazcaExporter();
        _componentLibrary = new ObservableCollection<ComponentTemplate>();
        _gdsExport = new GdsExportViewModel(new CAP_Core.Export.GdsExportService());
    }

    [Fact]
    public void HasUnsavedChanges_DefaultsToFalse()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);

        vm.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public void HasUnsavedChanges_SetToTrueWhenComponentAdded()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        _canvas.AddComponent(component, "TestTemplate");

        vm.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public void HasUnsavedChanges_SetToTrueWhenComponentRemoved()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        var componentVm = _canvas.AddComponent(component, "TestTemplate");
        vm.HasUnsavedChanges = false; // Reset after adding

        _canvas.Components.Remove(componentVm);

        vm.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task NewProject_ClearsCanvas_WhenNoUnsavedChanges()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);

        // Add some components
        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();
        _canvas.AddComponent(component1, "Template1");
        _canvas.AddComponent(component2, "Template2");

        vm.HasUnsavedChanges = false; // Simulate saved state

        await vm.NewProjectCommand.ExecuteAsync(null);

        _canvas.Components.Count.ShouldBe(0);
        _canvas.Connections.Count.ShouldBe(0);
        vm.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task NewProject_PromptsToSave_WhenHasUnsavedChanges()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);
        var mockMessageBox = new Mock<IMessageBoxService>();

        // Setup mock to return DontSave
        mockMessageBox
            .Setup(m => m.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.DontSave);

        vm.MessageBoxService = mockMessageBox.Object;

        // Add component and mark as unsaved
        var component = TestComponentFactory.CreateStraightWaveGuide();
        _canvas.AddComponent(component, "Template");
        vm.HasUnsavedChanges.ShouldBeTrue();

        await vm.NewProjectCommand.ExecuteAsync(null);

        // Verify prompt was shown
        mockMessageBox.Verify(
            m => m.ShowSavePromptAsync(
                It.Is<string>(s => s.Contains("save")),
                It.IsAny<string>()),
            Times.Once);

        // Canvas should be cleared
        _canvas.Components.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NewProject_CancelsOperation_WhenUserClicksCancel()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);
        var mockMessageBox = new Mock<IMessageBoxService>();

        // Setup mock to return Cancel
        mockMessageBox
            .Setup(m => m.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.Cancel);

        vm.MessageBoxService = mockMessageBox.Object;

        // Add component
        var component = TestComponentFactory.CreateStraightWaveGuide();
        _canvas.AddComponent(component, "Template");
        var initialCount = _canvas.Components.Count;

        await vm.NewProjectCommand.ExecuteAsync(null);

        // Canvas should NOT be cleared
        _canvas.Components.Count.ShouldBe(initialCount);
        vm.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task NewProject_CallsSave_WhenUserClicksSave()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);
        var mockMessageBox = new Mock<IMessageBoxService>();
        var mockFileDialog = new Mock<IFileDialogService>();

        // Setup mock to return Save
        mockMessageBox
            .Setup(m => m.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.Save);

        // Setup file dialog to return null (user cancels save)
        mockFileDialog
            .Setup(f => f.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        vm.MessageBoxService = mockMessageBox.Object;
        vm.FileDialogService = mockFileDialog.Object;

        // Add component
        var component = TestComponentFactory.CreateStraightWaveGuide();
        _canvas.AddComponent(component, "Template");

        await vm.NewProjectCommand.ExecuteAsync(null);

        // Verify save dialog was shown
        mockFileDialog.Verify(
            f => f.ShowSaveFileDialogAsync(
                It.Is<string>(s => s.Contains("Save")),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        // Canvas should NOT be cleared because save was cancelled
        _canvas.Components.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void HasUnsavedChanges_UpdatesStatus_OnPropertyChange()
    {
        var vm = new FileOperationsViewModel(_canvas, _commandManager, _nazcaExporter, _componentLibrary, _gdsExport);
        bool propertyChanged = false;

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.HasUnsavedChanges))
                propertyChanged = true;
        };

        vm.HasUnsavedChanges = true;

        propertyChanged.ShouldBeTrue();
    }
}
