using System.Collections.Generic;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Reviewed exact namespaces. New namespace descendants require an explicit ownership decision.</summary>
internal static class RepositoryModelNamespaces
{
    internal static IEnumerable<KeyValuePair<string, string>> Entries()
    {
        // CAP_Contracts
        yield return new("CAP_Contracts", RepositoryModules.Contracts);
        yield return new("CAP_Contracts.Logger", RepositoryModules.Contracts);
        // Connect-A-Pic-Core
        yield return new("CAP_Core", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.CircuitOptimization", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.EyeDiagram", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.Hierarchy", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.LogicAnalysis", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.LogicAnalysis.GateBuilding", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.LogicAnalysis.GateView", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.MeasuredSpectrum", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.MonteCarloAnalysis", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.OnaAnalysis", RepositoryModules.Domain);
        yield return new("CAP_Core.Analysis.WavelengthSpectrum", RepositoryModules.Domain);
        yield return new("CAP_Core.CodeExporter", RepositoryModules.Application);
        yield return new("CAP_Core.ComponentRegistry.RegistryClient", RepositoryModules.Contracts);
        yield return new("CAP_Core.Components.ComponentHelpers", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Connections", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Connections.Ownership", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Core", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Creation", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Dies", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.FormulaReading", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.GroupPins", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Parametric", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.PinKinds", RepositoryModules.Domain);
        yield return new("CAP_Core.Components.Process", RepositoryModules.Domain);
        yield return new("CAP_Core.Documents", RepositoryModules.Application);
        yield return new("CAP_Core.Export", RepositoryModules.Application);
        yield return new("CAP_Core.Export.InterconnectRouting", RepositoryModules.Application);
        yield return new("CAP_Core.Export.Netlist", RepositoryModules.Application);
        yield return new("CAP_Core.Export.PdkResolution", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.Processes", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.PythonEnvironmentManager", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.ExternalPorts", RepositoryModules.Domain);
        yield return new("CAP_Core.ExternalPorts.LaserSpectrum", RepositoryModules.Domain);
        yield return new("CAP_Core.Grid", RepositoryModules.Domain);
        yield return new("CAP_Core.Grid.FormulaReading", RepositoryModules.Domain);
        yield return new("CAP_Core.Grid.GroupCollision", RepositoryModules.Domain);
        yield return new("CAP_Core.Helpers", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.BendLoss", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.LaserSpectrum", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.MaterialDispersion", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.PowerFlow", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.Snapshots", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.TimeDomainSimulation", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.TimeDomainSimulation.CompactModels", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.TimeDomainSimulation.CompactModels.Models", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.TimeDomainSimulation.PhaseNoise", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.TimeDomainSimulation.Sampling", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.TimeDomainSimulation.Sources", RepositoryModules.Domain);
        yield return new("CAP_Core.LightCalculation.Validation", RepositoryModules.Domain);
        yield return new("CAP_Core.Logic.Isa", RepositoryModules.Domain);
        yield return new("CAP_Core.Preferences", RepositoryModules.Contracts);
        yield return new("CAP_Core.Routing", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.AStarPathfinder", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.AutoConnect", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.CrossingInsertion", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.CrossingInsertion.CrossingMove", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.CrossingInsertion.ManualCrossing", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.ImportedGeometry", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.InterconnectRouting", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.InterconnectRouting.SegmentShift", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.MeanderGeneration", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.MetalRouting", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.PinBreakout", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.RerouteImported", RepositoryModules.Domain);
        yield return new("CAP_Core.Routing.Snapshots", RepositoryModules.Application);
        yield return new("CAP_Core.Routing.WaveguideCompanions", RepositoryModules.Domain);
        yield return new("CAP_Core.Solvers.Fdtd", RepositoryModules.Domain);
        yield return new("CAP_Core.Solvers.ModeProbe", RepositoryModules.Domain);
        yield return new("CAP_Core.Solvers.ModeSolver", RepositoryModules.Domain);
        yield return new("CAP_Core.Solvers.Protocol", RepositoryModules.Application);
        yield return new("CAP_Core.Tiles", RepositoryModules.Domain);
        yield return new("CAP_Core.Update", RepositoryModules.Contracts);
        // CAP-DataAccess
        yield return new("CAP_DataAccess", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.AddCustomComponent", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.DTOs", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Helpers", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Import", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Import.Gds", RepositoryModules.Domain);
        yield return new("CAP_DataAccess.Import.Gds.Interpretation", RepositoryModules.Domain);
        yield return new("CAP_DataAccess.Import.Gds.LayerCensus", RepositoryModules.Domain);
        yield return new("CAP_DataAccess.Import.Gds.LayerPicker", RepositoryModules.Domain);
        yield return new("CAP_DataAccess.Persistence", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Persistence.DTOs", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Persistence.NativeDesign", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Persistence.PIR", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Updates", RepositoryModules.Infrastructure);
    }
}
