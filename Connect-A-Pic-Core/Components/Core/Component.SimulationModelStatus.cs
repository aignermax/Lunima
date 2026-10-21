namespace CAP_Core.Components.Core;

public partial class Component
{
    /// <summary>Explicit imported model readiness; geometry-only components must not participate in simulation.</summary>
    public string? SimulationModelStatus { get; set; }
}
