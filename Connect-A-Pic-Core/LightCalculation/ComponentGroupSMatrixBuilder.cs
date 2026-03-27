using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.LightCalculation;

/// <summary>
/// Builds S-Matrix representations for ComponentGroup instances by combining
/// child component S-Matrices and frozen internal waveguide paths.
/// </summary>
public class ComponentGroupSMatrixBuilder
{
    /// <summary>
    /// Computes the S-Matrix for a ComponentGroup at a specific wavelength.
    /// The resulting matrix maps external GroupPins to each other via internal components and paths.
    /// </summary>
    /// <param name="group">The ComponentGroup to compute S-Matrix for.</param>
    /// <param name="wavelengthNm">Wavelength in nanometers.</param>
    /// <returns>S-Matrix with GroupPin connections, or null if the group has no external pins.</returns>
    public Dictionary<int, SMatrix>? BuildGroupSMatrix(ComponentGroup group, int wavelengthNm)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));

        if (group.ExternalPins.Count == 0)
            return null;

        // Get all wavelengths that child components support
        var supportedWavelengths = GetSupportedWavelengths(group);

        if (supportedWavelengths.Count == 0)
            return null;

        var result = new Dictionary<int, SMatrix>();

        // Build S-Matrix for the requested wavelength
        var matrix = BuildSMatrixForWavelength(group, wavelengthNm);
        if (matrix != null)
        {
            result[wavelengthNm] = matrix;
        }

        return result;
    }

    /// <summary>
    /// Builds S-Matrices for all wavelengths supported by child components.
    /// </summary>
    public Dictionary<int, SMatrix>? BuildGroupSMatrixAllWavelengths(ComponentGroup group)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));

        if (group.ExternalPins.Count == 0)
            return null;

        var supportedWavelengths = GetSupportedWavelengths(group);

        if (supportedWavelengths.Count == 0)
            return null;

        var result = new Dictionary<int, SMatrix>();

        foreach (var wavelength in supportedWavelengths)
        {
            var matrix = BuildSMatrixForWavelength(group, wavelength);
            if (matrix != null)
            {
                result[wavelength] = matrix;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all wavelengths supported by child components in the group.
    /// </summary>
    private HashSet<int> GetSupportedWavelengths(ComponentGroup group)
    {
        var wavelengths = new HashSet<int>();

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
            {
                // Recursively get wavelengths from nested groups
                foreach (var wl in GetSupportedWavelengths(childGroup))
                {
                    wavelengths.Add(wl);
                }
            }
            else
            {
                foreach (var wl in child.WaveLengthToSMatrixMap.Keys)
                {
                    wavelengths.Add(wl);
                }
            }
        }

        return wavelengths;
    }

    /// <summary>
    /// Builds the S-Matrix for a specific wavelength.
    /// Combines child S-Matrices and frozen path connections.
    /// </summary>
    private SMatrix? BuildSMatrixForWavelength(ComponentGroup group, int wavelengthNm)
    {
        // Collect all physical pin IDs from child components
        var allChildPinIds = new List<Guid>();

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
            {
                // For nested groups, use their external pins
                foreach (var groupPin in childGroup.ExternalPins)
                {
                    allChildPinIds.Add(groupPin.InternalPin.LogicalPin.IDInFlow);
                    allChildPinIds.Add(groupPin.InternalPin.LogicalPin.IDOutFlow);
                }
            }
            else
            {
                // For regular components, use physical pins
                foreach (var pin in child.PhysicalPins)
                {
                    allChildPinIds.Add(pin.LogicalPin.IDInFlow);
                    allChildPinIds.Add(pin.LogicalPin.IDOutFlow);
                }
            }
        }

        if (allChildPinIds.Count == 0)
            return null;

        // Create system matrix from all child components
        var childMatrices = new List<SMatrix>();

        foreach (var child in group.ChildComponents)
        {
            SMatrix? childMatrix = GetChildSMatrix(child, wavelengthNm);
            if (childMatrix != null)
            {
                childMatrices.Add(childMatrix);
            }
        }

        // Add connections from frozen internal paths
        var internalConnections = BuildInternalConnectionMatrix(group, allChildPinIds);
        if (internalConnections != null)
        {
            childMatrices.Add(internalConnections);
        }

        if (childMatrices.Count == 0)
            return null;

        // Combine all matrices into a system matrix
        var systemMatrix = SMatrix.CreateSystemSMatrix(childMatrices);

        // Create the external pin mapping
        var externalPinIds = new List<Guid>();
        foreach (var extPin in group.ExternalPins)
        {
            externalPinIds.Add(extPin.InternalPin.LogicalPin.IDInFlow);
            externalPinIds.Add(extPin.InternalPin.LogicalPin.IDOutFlow);
        }

        // Extract the sub-matrix for external pins only
        var groupMatrix = ExtractExternalPinMatrix(systemMatrix, externalPinIds);

        return groupMatrix;
    }

    /// <summary>
    /// Gets the S-Matrix for a child component at the specified wavelength.
    /// </summary>
    private SMatrix? GetChildSMatrix(Component child, int wavelengthNm)
    {
        if (child is ComponentGroup childGroup)
        {
            // Recursively build S-Matrix for nested groups
            var childGroupMatrices = BuildGroupSMatrixAllWavelengths(childGroup);
            if (childGroupMatrices != null && childGroupMatrices.TryGetValue(wavelengthNm, out var matrix))
            {
                return matrix;
            }

            // Try nearest wavelength fallback
            if (childGroupMatrices != null && childGroupMatrices.Count > 0)
            {
                var nearestWl = childGroupMatrices.Keys
                    .OrderBy(k => Math.Abs(k - wavelengthNm))
                    .First();
                return childGroupMatrices[nearestWl];
            }

            return null;
        }

        if (child.WaveLengthToSMatrixMap.TryGetValue(wavelengthNm, out var childMatrix))
        {
            return childMatrix;
        }

        // Fallback to nearest wavelength
        if (child.WaveLengthToSMatrixMap.Count > 0)
        {
            var nearestKey = child.WaveLengthToSMatrixMap.Keys
                .OrderBy(k => Math.Abs(k - wavelengthNm))
                .First();
            return child.WaveLengthToSMatrixMap[nearestKey];
        }

        return null;
    }

    /// <summary>
    /// Builds a connection matrix for frozen internal waveguide paths.
    /// </summary>
    private SMatrix? BuildInternalConnectionMatrix(ComponentGroup group, List<Guid> allPinIds)
    {
        if (group.InternalPaths.Count == 0)
            return null;

        var connections = new Dictionary<(Guid, Guid), Complex>();

        foreach (var frozenPath in group.InternalPaths)
        {
            // Frozen paths connect pins bidirectionally with unity transfer (no loss)
            var startInFlow = frozenPath.StartPin.LogicalPin.IDInFlow;
            var startOutFlow = frozenPath.StartPin.LogicalPin.IDOutFlow;
            var endInFlow = frozenPath.EndPin.LogicalPin.IDInFlow;
            var endOutFlow = frozenPath.EndPin.LogicalPin.IDOutFlow;

            // Forward: light flows from StartPin OutFlow to EndPin InFlow
            connections[(startOutFlow, endInFlow)] = Complex.One;
            // Reverse: light flows from EndPin OutFlow to StartPin InFlow (bidirectional)
            connections[(endOutFlow, startInFlow)] = Complex.One;
        }

        if (connections.Count == 0)
            return null;

        var connectionMatrix = new SMatrix(allPinIds, new());
        connectionMatrix.SetValues(connections);

        return connectionMatrix;
    }

    /// <summary>
    /// Extracts an effective sub-matrix for the specified external pins by running
    /// the internal system matrix through enough propagation steps to capture all
    /// multi-hop paths (e.g. comp1 → internal connection → comp2).
    /// </summary>
    private SMatrix ExtractExternalPinMatrix(SMatrix systemMatrix, List<Guid> externalPinIds)
    {
        // Compute the effective transfer: A + A^2 + ... + A^n
        // This captures all multi-hop paths through the group's internal components.
        // Single-hop read (A only) misses paths that go through intermediate pins.
        int maxSteps = systemMatrix.PinReference.Count * 2;
        var A = systemMatrix.SMat;
        var runningPower = A;
        var effectiveA = A.Clone();

        for (int i = 1; i < maxSteps; i++)
        {
            runningPower = A * runningPower;
            effectiveA += runningPower;
        }

        var externalMatrix = new SMatrix(externalPinIds, new());
        var transfers = new Dictionary<(Guid, Guid), Complex>();

        // Extract the effective transfers for external pins only
        foreach (var pinIn in externalPinIds)
        {
            foreach (var pinOut in externalPinIds)
            {
                if (pinIn == pinOut)
                    continue;

                if (systemMatrix.PinReference.TryGetValue(pinIn, out int idxIn) &&
                    systemMatrix.PinReference.TryGetValue(pinOut, out int idxOut))
                {
                    var value = effectiveA[idxOut, idxIn];
                    if (value != Complex.Zero)
                    {
                        transfers[(pinIn, pinOut)] = value;
                    }
                }
            }
        }

        externalMatrix.SetValues(transfers);
        return externalMatrix;
    }
}
