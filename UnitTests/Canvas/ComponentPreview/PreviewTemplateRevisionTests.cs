using System.Collections.ObjectModel;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewTemplateRevisionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "preview-revision-" + Guid.NewGuid());

    [Fact]
    public async Task WarmFrames_DoNotRescanTemplates_AndExplicitEditInvalidatesCode()
    {
        var lookups = 0;
        var code = "first";
        var service = new GdsPreviewRenderService(new Renderer(), new GdsPreviewDiskCache(_directory))
        {
            InlineNazcaCodeLookup = (_, _) => { lookups++; return code; }
        };
        var key = new GdsPreviewKey("pdk", "cell", "");
        service.TryGetGeometry(key);
        await service.WaitForPendingAsync();
        for (var frame = 0; frame < 100; frame++) service.TryGetGeometry(key)!.XMax.ShouldBe(1);
        lookups.ShouldBe(1);

        code = "edited";
        service.InvalidatePdkPreviews();
        service.TryGetGeometry(key).ShouldBeNull();
        await service.WaitForPendingAsync();
        for (var frame = 0; frame < 100; frame++) service.TryGetGeometry(key)!.XMax.ShouldBe(2);
        lookups.ShouldBe(2);
    }

    [Fact]
    public void SavingExistingPdkDefinition_RefreshesBackendRevisionWithoutReplacingManagerRow()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "custom.json");
        File.WriteAllText(file, "original");
        var manager = new PdkManagerViewModel();
        manager.RegisterPdk("Custom", file, false, 1);
        var originalRow = manager.LoadedPdks.Single();
        var preferences = new UserPreferencesService(Path.Combine(_directory, "prefs.json"));
        var backend = new Renderer();
        PreviewBackendConfiguration.Configure(backend, preferences, manager);
        var before = backend.EnvironmentIdentity;
        var draft = new PdkComponentDraft { Name = "cell", WidthMicrometers = 1, HeightMicrometers = 1 };
        var cached = new PdkDraft { Name = "Custom", FilePath = file, Components = [draft] };
        File.WriteAllText(file, "updated component definition");

        CustomComponentLibraryRegistrar.Register(draft, "Custom", file,
            new ObservableCollection<ComponentTemplate>(), new ObservableCollection<string>(),
            manager, preferences, new PdkLoader(), [cached], () => { }, () => { });

        manager.LoadedPdks.Single().ShouldBeSameAs(originalRow);
        backend.EnvironmentIdentity.ShouldNotBe(before);
    }

    private sealed class Renderer() : NazcaComponentPreviewService("unused", "missing-script.py")
    {
        public override Task<NazcaPreviewResult> RenderRawCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(new NazcaPreviewResult
            {
                Success = true, XMax = code == "first" ? 1 : 2, YMax = 1,
                Polygons = [new() { Vertices = [(0, 0), (1, 0), (0, 1)] }]
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
