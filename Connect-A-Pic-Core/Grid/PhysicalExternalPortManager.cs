using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace CAP_Core.Grid;

/// <summary>
/// IExternalPortManager implementation for physical-coordinate simulation mode.
/// Light sources are added explicitly by mapping to component pin IDs,
/// rather than discovering them via tile grid positions.
/// </summary>
public class PhysicalExternalPortManager : IExternalPortManager
{
    private readonly List<(ExternalInput Input, Guid PinId)> _lightSources = new();

    public ObservableCollection<ExternalPort> ExternalPorts { get; set; } = new();

    /// <summary>
    /// Adds a light source at a specific component pin.
    /// </summary>
    /// <param name="input">The external input (laser type, power).</param>
    /// <param name="componentPinIdInFlow">The IDInFlow of the target pin.</param>
    public void AddLightSource(ExternalInput input, Guid componentPinIdInFlow)
    {
        _lightSources.Add((input, componentPinIdInFlow));
        if (!ExternalPorts.Contains(input))
            ExternalPorts.Add(input);
    }

    public ConcurrentBag<ExternalInput> GetAllExternalInputs()
    {
        var bag = new ConcurrentBag<ExternalInput>();
        foreach (var (input, _) in _lightSources)
            bag.Add(input);
        return bag;
    }

    public ConcurrentBag<UsedInput> GetUsedExternalInputs()
    {
        var bag = new ConcurrentBag<UsedInput>();
        foreach (var (input, pinId) in _lightSources)
            bag.Add(new UsedInput(input, pinId));
        return bag;
    }

    public void Clear()
    {
        _lightSources.Clear();
        ExternalPorts.Clear();
    }
}
