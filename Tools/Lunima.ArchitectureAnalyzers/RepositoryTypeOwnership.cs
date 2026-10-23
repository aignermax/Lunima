using System.Collections.Generic;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Mixed legacy namespaces: explicit ownership follows responsibility, not the containing project.</summary>
internal static class RepositoryTypeOwnership
{
    internal static IEnumerable<KeyValuePair<string, string>> Entries()
    {
        // Domain responsibilities in namespaces that also contain another layer.
        yield return new("CAP_Core.Export.NazcaCoordinateMapper", RepositoryModules.Domain);
        yield return new("CAP_Core.Export.CellPlacement", RepositoryModules.Domain);
        yield return new("CAP_Core.Export.MetalTraceStyle", RepositoryModules.Domain);
        yield return new("CAP_Core.ComponentRegistry.RegistryClient.SParameterSpectrum", RepositoryModules.Domain);
        yield return new("CAP.Avalonia.Services.ComponentGeometryKey", RepositoryModules.Domain);
        yield return new("CAP.Avalonia.Services.GroupPreviewGenerator", RepositoryModules.Domain);
        yield return new("CAP.Avalonia.Services.ImportedModelReadiness", RepositoryModules.Domain);
        yield return new("CAP.Avalonia.Services.TemplateGeometryMatch", RepositoryModules.Domain);
        yield return new("CAP_DataAccess.Import.Gds.GdsSourceReader", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Import.Gds.IGdsSourceReader", RepositoryModules.Contracts);
        // Contracts responsibilities in namespaces that also contain another layer.
        yield return new("CAP_Core.Export.PdkParseResult", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.ParsedComponentGeometry", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.ParsedPinGeometry", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.IPdkImportWorkflow", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.NazcaPreviewPolygon", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.NazcaPreviewPin", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.NazcaPreviewResult", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.VerilogAExportOptions", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.VerilogAExportResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.IFileDialogService", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.IInputDialogService", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.IMessageBoxService", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.IPortMappingDialogService", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.IUrlLauncher", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.DesignFileArguments", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.SourceConfigInfo", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.UserPreferences", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.AgentBridge.Hosting.IUiDispatcher", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.AgentToolResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.Update.IInstaller", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.Solvers.IDockerSetupDialogService", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.IProcessImporter", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.PdkTolerantLoadResult", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.PdkValidationException", RepositoryModules.Contracts);
        yield return new("CAP_Core.ComponentRegistry.RegistryClient.RegistryPreviewSvgParser", RepositoryModules.Application);
        yield return new("CAP_Core.ComponentRegistry.RegistryClient.RegistryClient", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.ComponentRegistry.RegistryClient.RegistryCache", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.ExamplesManifestEntry", RepositoryModules.Contracts);
        // Infrastructure responsibilities in namespaces that also contain another layer.
        yield return new("CAP_Core.Helpers.FileDataAccessor", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Helpers.AppDataFolders", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Components.Creation.GroupLibraryManager", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.ExecutablePathProber", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.GdsCoordinateComparisonService", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.GdsCoordinateExtractor", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.GdsExportService", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.GdsFactoryComponentPreviewService", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.NazcaComponentPreviewService", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.NazcaReferenceGenerator", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.PdkNazcaParserService", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.PreviewEnvironmentIdentity", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.ProcessLaunchFactory", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.PythonDiscoveryService", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.PythonInstallPathScanner", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Export.PythonModuleShadowing", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkPaths", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.NazcaCsvProcessImporter", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.PdkJsonSaver", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.PdkLoader", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.UpdkYamlProcessImporter", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Persistence.NativeDesign.NativeDesignFileStore", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.PdkImportService", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.PlatformShellLauncher", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.PythonResolution", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.RecentProjectsService", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.UserPreferencesService", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.UserSMatrixOverrideStore", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.VerilogAFileWriter", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.AgentBridge.Contracts.AgentBridgeAddress", RepositoryModules.Application);
        // Application responsibilities in namespaces that also contain another layer.
        yield return new("CAP_DataAccess.Persistence.NativeDesign.NativeComponentStateMapper", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Persistence.NativeDesign.NativeEndpointMapper", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Persistence.NativeDesign.NativeGroupOrder", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AsDrawnNazcaWriter", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.BuiltLogicNetworkProvider", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.NazcaCodeTemplateBuilder", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.NazcaStubNaming", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.SiepicWaveguideCellWriter", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.SMatrixOverrideApplicator", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.SMatrixOverrideGc", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Hosting.AgentOperationGate", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.AgentToolContext", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.AgentToolCatalog", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.IAgentBridgeTool", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.ToolArguments", RepositoryModules.Application);
        // Registry exchange records and capabilities have no transport or persistence behavior.
        yield return new("CAP.Avalonia.Services.ComponentRegistry.IRegistryAdoption", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ComponentRegistry.RegistryDownloadResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ComponentRegistry.RegistryArtifactChoice", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ComponentRegistry.RegistryArtifactSelector", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.ComponentRegistry.RegistryComponentDraftMapper", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.Solvers.FdtdJsonContract", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.Solvers.FdtdSMatrixConverter", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.Solvers.Tidy3dJsonContract", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.Solvers.ComponentFdtdRequestFactory", RepositoryModules.Application);
        yield return new("CAP_Core.Export.FoundryEnvironmentErrorHint", RepositoryModules.Application);
        yield return new("CAP_Core.Export.PreviewGeometryCache", RepositoryModules.Application);
        // Presentation responsibilities in namespaces that also contain another layer.
        yield return new("CAP.Avalonia.Services.AgentBridge.Hosting.AvaloniaUiDispatcher", RepositoryModules.Presentation);
        yield return new("CAP.Avalonia.Services.Solvers.DockerSetupDialogService", RepositoryModules.Presentation);
        yield return new("CAP.Avalonia.Services.Solvers.FdtdBackendRegistry", RepositoryModules.Presentation);
    }
}
