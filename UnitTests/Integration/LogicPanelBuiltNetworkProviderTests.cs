using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// The Logic panel's half of the rung-5 hand-off (issue #1215): a successful
/// "Build logic network" publishes the assembled network to the shared
/// <see cref="BuiltLogicNetworkProvider"/> (what the ISA playground's photonic-ADD
/// toggle consumes), and a design edit that invalidates the shown network clears
/// the provider again — the playground must never compute on a design that no
/// longer exists. Uses the shipped half adder, whose network the photonic ALU
/// rightly rejects (it lacks the A0–A3/B0–B3/Cin/S0–S3 signals).
/// </summary>
public class LogicPanelBuiltNetworkProviderTests : IClassFixture<LogicPanelViewModelTests.LoadedHalfAdder>
{
    private readonly LogicPanelViewModelTests.LoadedHalfAdder _fixture;

    /// <summary>Attaches the shared loaded half-adder canvas.</summary>
    public LogicPanelBuiltNetworkProviderTests(LogicPanelViewModelTests.LoadedHalfAdder fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task BuildNetwork_PublishesAssembledNetwork_ToSharedProvider()
    {
        var provider = new BuiltLogicNetworkProvider();
        var vm = new LogicPanelViewModel(builtNetworkProvider: provider);
        vm.Configure(_fixture.Canvas);

        await vm.BuildNetworkCommand.ExecuteAsync(null);

        vm.HasNetwork.ShouldBeTrue(vm.StatusText);
        provider.Network.ShouldNotBeNull("a successful build hands the network to the playground");
        PhotonicAdderAlu.Accepts(provider.Network).ShouldBeFalse(
            "the half adder lacks the 4-bit-adder signals, so the playground toggle stays disabled");
    }

    [Fact]
    public async Task DesignEdit_AfterBuild_ClearsPublishedNetwork()
    {
        var provider = new BuiltLogicNetworkProvider();
        var vm = new LogicPanelViewModel(builtNetworkProvider: provider);
        vm.Configure(_fixture.Canvas);
        await vm.BuildNetworkCommand.ExecuteAsync(null);
        provider.Network.ShouldNotBeNull();

        _fixture.Canvas.Components.RemoveAt(0);

        provider.Network.ShouldBeNull("a design edit invalidates the published network");
        vm.HasNetwork.ShouldBeFalse();
    }
}
