using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_Core.Components.Core;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Persistence.PIR;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.ComponentSettings;

/// <summary>
/// Verifies the FDTD "Recalculate S-matrix" command on the Component Settings
/// dialog: gating, success (stores + status) and failure (status, no store).
/// </summary>
public class ComponentSettingsDialogFdtdTests
{
    private static Func<Component, CancellationToken, Task<FdtdSMatrixRequest?>> FakeFactory() =>
        (_, _) => Task.FromResult<FdtdSMatrixRequest?>(new FdtdSMatrixRequest
        {
            Ports = new[] { new FdtdPort { Name = "o1" }, new FdtdPort { Name = "o2" } },
        });

    private static FdtdSMatrixResult SuccessResult() => new()
    {
        Success = true,
        Is3D = false,
        Ports = new[] { "o1", "o2" },
        Wavelengths = new[] { 1.55 },
        Entries = new[]
        {
            new FdtdSEntry { Key = "o2@0,o1@0", Values = new[] { new Complex(0.95, 0.0) } },
            new FdtdSEntry { Key = "o1@0,o2@0", Values = new[] { new Complex(0.95, 0.0) } },
        },
        EnergySumPerInput = new Dictionary<string, double> { ["o1@0"] = 0.97, ["o2@0"] = 0.97 },
    };

    [Fact]
    public void CanRecalculate_IsFalse_WithoutFdtdWiring()
    {
        var vm = new ComponentSettingsDialogViewModel(Mock.Of<IFileDialogService>());
        vm.Configure("c", "C", new Dictionary<string, ComponentSMatrixData>(),
            liveComponent: TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());

        vm.CanRecalculate.ShouldBeFalse();
    }

    [Fact]
    public async Task RecalculateSMatrix_OnSuccess_StoresDataAndReportsStatus()
    {
        var service = new Mock<IFdtdSMatrixService>();
        service.Setup(s => s.SolveAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(SuccessResult());
        var store = new Dictionary<string, ComponentSMatrixData>();

        var vm = new ComponentSettingsDialogViewModel(
            Mock.Of<IFileDialogService>(),
            fdtdService: service.Object,
            fdtdRequestFactory: FakeFactory());
        vm.Configure("comp", "Comp", store,
            liveComponent: TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());

        vm.CanRecalculate.ShouldBeTrue();
        await vm.RecalculateSMatrixCommand.ExecuteAsync(null);

        store.ShouldContainKey("comp");
        store["comp"].Wavelengths.ShouldContainKey("1550");
        vm.SolverStatus.ShouldContain("FDTD done");
        vm.IsComputing.ShouldBeFalse();
    }

    [Fact]
    public async Task RecalculateSMatrix_OnFailure_SurfacesHintAndStoresNothing()
    {
        var service = new Mock<IFdtdSMatrixService>();
        service.Setup(s => s.SolveAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FdtdSMatrixResult.Fail("image build failed", missingDependency: "docker"));
        var store = new Dictionary<string, ComponentSMatrixData>();

        var vm = new ComponentSettingsDialogViewModel(
            Mock.Of<IFileDialogService>(),
            fdtdService: service.Object,
            fdtdRequestFactory: FakeFactory());
        vm.Configure("comp", "Comp", store,
            liveComponent: TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());

        await vm.RecalculateSMatrixCommand.ExecuteAsync(null);

        store.ShouldNotContainKey("comp");
        vm.SolverStatus.ShouldContain("docker");
    }
}
