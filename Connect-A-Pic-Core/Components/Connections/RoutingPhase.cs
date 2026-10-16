namespace CAP_Core.Components.Connections;

/// <summary>The current stage of a routing pass, independent of connection completion counts.</summary>
public enum RoutingPhase
{
    Initial,
    CrossingInsertion,
    Finishing,
    ContentionRepair
}
