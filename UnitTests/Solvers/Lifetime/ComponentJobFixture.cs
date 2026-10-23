using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP.Avalonia.Services.Notifications;
using CAP_Core;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_Core.Documents;
using CAP_Core.Export;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Persistence.PIR;
using Moq;

namespace UnitTests.Solvers.Lifetime;

internal sealed class ComponentJobFixture
{
    public RetirementBarrier Barrier { get; } = new();
    public Mock<IFdtdSMatrixService> Solver { get; } = new();
    public Mock<IComponentPreviewRenderer> Renderer { get; } = new();
    public Mock<IUserPdkLibrary> Library { get; } = new();
    public Mock<IFileDialogService> Files { get; } = new();
    public CAP_Core.Components.Core.Component Target { get; } = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
    public Dictionary<string, ComponentSMatrixData> Store { get; } = new();
    public int Propagations { get; private set; }
    public int Changes { get; private set; }
    public string Stage { get; }
    public bool Cloud { get; }

    public ComponentJobFixture(string stage, bool cloud = false)
    {
        Stage = stage;
        Cloud = cloud;
        Renderer.Setup(renderer => renderer.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) =>
            { await PauseAsync("preview", ct); return Geometry(); });
        Solver.Setup(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            { await PauseAsync("availability", ct); return FdtdAvailability.Available("ready"); });
        Solver.Setup(service => service.SolveAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (FdtdSMatrixRequest _, IProgress<string>? _, CancellationToken ct) =>
            { await PauseAsync("solve", ct); return Result(); });
        if (cloud)
            Solver.As<IFdtdCostEstimator>()
                .Setup(service => service.EstimateCostAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (FdtdSMatrixRequest _, CancellationToken ct) =>
                { await PauseAsync("estimate", ct); return new FdtdCostEstimate { Success = true, EstimatedCredits = 1, SimulationCount = 2 }; });
    }

    private Task PauseAsync(string stage, CancellationToken ct) => stage == Stage ? Barrier.WaitAsync(ct) : Task.CompletedTask;

    public NewComponentViewModel NewEditor()
    {
        var process = new ProcessDefinition { Name = "fixture" };
        Library.Setup(store => store.ListCustomPdks()).Returns(new[] { new UserPdkInfo("fixture", "fixture.json", process) });
        return new NewComponentViewModel(new ComponentGeometryExtractor(Renderer.Object, Renderer.Object),
            Solver.Object, Library.Object, new[] { process }) { ComponentName = "Component" };
    }

    public ComponentSettingsDialogViewModel Settings(DocumentSession? document = null,
        INotificationService? notifications = null, ErrorConsoleService? errors = null,
        Func<CAP_Core.Components.Core.Component, bool>? isComponentPresent = null)
    {
        var model = new ComponentSettingsDialogViewModel(Files.Object, errorConsole: errors,
            notificationService: notifications, fdtdService: Solver.Object,
            fdtdRequestFactory: async (_, ct) =>
            {
                await PauseAsync("geometry", ct);
                return new FdtdSMatrixRequest { Ports = [new() { Name = "in" }, new() { Name = "out" }] };
            });
        model.Configure("component", "component", "Component", Store,
            liveComponent: Target,
            onChanged: () => Changes++,
            propagateToTemplate: _ => { Propagations++; return true; }, document: document,
            isComponentPresent: isComponentPresent);
        return model;
    }

    public async Task StartAsync(NewComponentViewModel model)
    {
        if (Stage == "preview") { await model.RunPreviewCommand.ExecuteAsync(null); return; }
        if (Cloud && Stage == "solve")
        {
            await model.ComputeSMatrixCommand.ExecuteAsync(null);
            await model.ConfirmCloudSubmitCommand.ExecuteAsync(null);
        }
        else await model.ComputeSMatrixCommand.ExecuteAsync(null);
    }

    public async Task StartAsync(ComponentSettingsDialogViewModel model)
    {
        if (Cloud && Stage == "solve")
        {
            await model.RecalculateSMatrixCommand.ExecuteAsync(null);
            await model.ConfirmCloudSubmitCommand.ExecuteAsync(null);
        }
        else await model.RecalculateSMatrixCommand.ExecuteAsync(null);
    }

    private static NazcaPreviewResult Geometry() => new()
    {
        Success = true, XMax = 4, YMax = 2,
        Polygons = [new() { Layer = 1, Vertices = [(0, 0), (4, 0), (4, 2)] }],
        Pins = [new() { Name = "in", X = 0, Y = 1, Angle = 180 }, new() { Name = "out", X = 4, Y = 1 }],
    };

    private static FdtdSMatrixResult Result() => new()
    {
        Success = true, Ports = ["in", "out"], Wavelengths = [1.55],
        Entries = [new() { Key = "out@0,in@0", Values = [new Complex(.95, 0)] },
            new() { Key = "in@0,out@0", Values = [new Complex(.95, 0)] }],
        EnergySumPerInput = new Dictionary<string, double> { ["in@0"] = .95, ["out@0"] = .95 },
    };
}
