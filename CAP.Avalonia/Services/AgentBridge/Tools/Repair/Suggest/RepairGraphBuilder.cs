using CAP_Core.Routing.Snapshots;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Reconstructs mutable graph objects exclusively from detached routing values.</summary>
internal class RepairGraphBuilder
{
    internal virtual RoutingWorkGraph Build(RoutingInputSnapshot input) => input.CreatePrivateGraph();
}
