using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.PdkImport;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Architecture;

public sealed class PdkImportWorkflowBoundaryTests
{
    [Fact]
    public async Task ImportCapability_ReusesTheConfiguredParserAndPersistenceOwner()
    {
        await using var services = ProductionContainerTestHelper.BuildWithTempPreferences(
            Path.Combine(Path.GetTempPath(), $"lunima-import-di-{Guid.NewGuid():N}.json"));
        services.GetRequiredService<IPdkImportWorkflow>().ShouldBeSameAs(services.GetRequiredService<PdkImportService>());
    }

    [Fact]
    public async Task Wizard_UsesOnlyTheImportContract_AndPublishesCompletionAfterSelectedCellsAreSaved()
    {
        var first = new ParsedComponentGeometry { Name = "same-name", WidthMicrometers = 12 };
        var second = new ParsedComponentGeometry { Name = "same-name", WidthMicrometers = 24 };
        var imports = new ControlledImport(new PdkParseResult
        {
            Name = "Parsed library", FileFormatVersion = 3, NazcaModuleName = "source_module", Components = [first, second]
        });
        var vm = new PdkImportWizardViewModel("source.py", imports);
        string? completed = null;
        vm.OnCompleted = path => completed = path;
        await vm.StartParsingAsync();
        imports.ParsedPath.ShouldBe("source.py");
        vm.CurrentStep.ShouldBe(WizardStep.Review);
        vm.ParsedComponents.Count.ShouldBe(2);
        vm.PdkName.ShouldBe("Parsed library");
        vm.ParsedComponents[0].IsSelected = false;
        vm.PdkName = "Selected library";
        vm.OutputPath = "selected.json";
        var saving = vm.SaveAndLoadCommand.ExecuteAsync(null);
        try
        {
            saving.IsCompleted.ShouldBeFalse();
            completed.ShouldBeNull();
            vm.IsLoading.ShouldBeTrue();
            imports.Converted.ShouldNotBeNull();
            imports.Converted.Name.ShouldBe("Selected library");
            imports.Converted.NazcaModuleName.ShouldBe("source_module");
            imports.Converted.FileFormatVersion.ShouldBe(3);
            imports.Converted.Components.ShouldHaveSingleItem().ShouldBeSameAs(second);
            imports.SavedPath.ShouldBe("selected.json");
            imports.SavedDraft.ShouldBeSameAs(imports.Draft);
            imports.SaveFinished.TrySetResult();
            await saving;
            completed.ShouldBe("selected.json");
            vm.IsLoading.ShouldBeFalse();
            vm.HasError.ShouldBeFalse();
        }
        finally { imports.SaveFinished.TrySetResult(); await saving; }
    }

    private sealed class ControlledImport(PdkParseResult parsed) : IPdkImportWorkflow
    {
        internal string? ParsedPath { get; private set; }
        internal PdkParseResult? Converted { get; private set; }
        internal PdkDraft Draft { get; } = new() { Name = "Converted library" };
        internal PdkDraft? SavedDraft { get; private set; }
        internal string? SavedPath { get; private set; }
        internal TaskCompletionSource SaveFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PdkParseResult> ParseFromFileAsync(string pyFilePath, IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ParsedPath = pyFilePath;
            return Task.FromResult(parsed);
        }
        public PdkDraft ConvertToPdkDraft(PdkParseResult result) { Converted = result; return Draft; }
        public Task SaveToJsonAsync(PdkDraft draft, string outputPath)
        {
            SavedDraft = draft; SavedPath = outputPath;
            return SaveFinished.Task;
        }
    }
}
