using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using UnitTests.Analysis.LogicAnalysis;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

internal sealed class LogicLifetimeFixture : IAsyncDisposable
{
    public DesignCanvasViewModel Canvas { get; } = new();
    public BuiltLogicNetworkProvider Provider { get; } = new();
    public ControlledAssembler Assembler { get; } = new();
    public OwnedClock Clock { get; } = new();
    public LogicPanelViewModel Model { get; }

    public LogicLifetimeFixture(ApplicationWorkCoordinator? application = null)
    {
        Model = new LogicPanelViewModel(Clock, Provider, Assembler, application);
        Model.Configure(Canvas);
    }

    public Task BuildAsync() => Model.BuildNetworkCommand.ExecuteAsync(null);

    public async ValueTask DisposeAsync()
    {
        Assembler.Barrier.Release.TrySetResult();
        await Model.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Model.StopRun();
        await Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Canvas.DisposeDocumentBindings();
    }

    public static LogicNetworkEvaluator Network() => new(
        ["x"], new Dictionary<string, LogicGateModel> { ["reg"] = PinnedGateTables.NotGate() },
        new Dictionary<LogicPinRef, LogicNetDriver> { [new("reg", "A")] = new LogicNetDriver.NetworkInput("x") },
        new Dictionary<string, LogicPinRef> { ["q"] = new("reg", "Y") },
        registerGateIds: ["reg"]);

    internal sealed class ControlledAssembler : ILogicNetworkAssembler
    {
        public RetirementBarrier Barrier { get; } = new();
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public async Task<LogicNetworkEvaluator> AssembleAsync(IReadOnlyList<Component> components,
            IReadOnlyList<WaveguideConnection> connections, int wavelengthNm, CancellationToken cancellationToken = default)
        {
            Calls++;
            await Barrier.WaitAsync(cancellationToken);
            if (Fail) throw new InvalidOperationException("Controlled late logic failure");
            return Network();
        }
    }

    internal sealed class OwnedClock : ILogicRunClock
    {
        private EventHandler? _tick;
        public int Subscribers => _tick?.GetInvocationList().Length ?? 0;
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add => _tick += value; remove => _tick -= value; }
        public void Start(TimeSpan interval) => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() => _tick?.Invoke(this, EventArgs.Empty);
    }
}
