using System.Collections.Generic;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Reviewed import/worker values and ports, distinct from the side-effect owners beside them.</summary>
internal static class RepositoryBoundaryOwnership
{
    internal static IEnumerable<KeyValuePair<string, string>> Entries()
    {
        // Pure release selection policy, distinct from network retrieval.
        yield return new("CAP_Core.Update.ReleaseAssetPolicy", RepositoryModules.Application);
        // Infrastructure
        yield return new("CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumFileSource", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Logger", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Analysis.SweepCsvExporter", RepositoryModules.Infrastructure);
        yield return new("CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumCsvReader", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Import.Gds.GdsReader", RepositoryModules.Infrastructure);
        yield return new("CAP_DataAccess.Import.Gds.GdsRecordReader", RepositoryModules.Infrastructure);
        yield return new("CAP.Avalonia.Services.ExamplesManifest", RepositoryModules.Infrastructure);
        // Application
        yield return new("CAP_DataAccess.Persistence.NativeDesign.NativeDesignCodec", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Import.Gds.GdsHierarchyImporter", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Import.Gds.GdsHierarchyImportSession", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Import.Gds.GdsImportReporter", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Import.PortNameMapping", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Import.SParameterConverter", RepositoryModules.Application);
        yield return new("CAP_DataAccess.Persistence.ComponentGroupSerializer", RepositoryModules.Application);
        yield return new("CAP_Core.Export.PythonEnvironmentManager.EnvironmentNaming", RepositoryModules.Application);
        yield return new("CAP_Core.Export.PdkResolution.NazcaFunctionPath", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Protocol.McpJsonRpcDispatcher", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.AiToolAdapter", RepositoryModules.Application);
        yield return new("CAP.Avalonia.Services.GdsFactoryExport.GdsFactoryPreviewCode", RepositoryModules.Application);
        // Contracts
        yield return new("CAP_Core.Analysis.MeasuredSpectrum.IMeasuredSpectrumSource", RepositoryModules.Contracts);
        yield return new("CAP_Core.Components.Creation.IGroupLibrary", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Persistence.PIR.ITemplateSMatrixStore", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Persistence.NativeDesign.INativeDesignFileStore", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.IPdkReader", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.ComponentDraftMapper.IPdkWriter", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.IPdkGeometryRenderer", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.IUserPdkLibrary", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.IPdkTrash", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PythonEnvironmentManager.PythonEnvironment", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PythonEnvironmentManager.PythonEnvironmentStatus", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PythonEnvironmentManager.IManagedPythonProvisioner", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PdkResolution.PdkResolutionStatus", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PdkResolution.PdkResolutionEntry", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PdkResolution.PdkResolutionResult", RepositoryModules.Contracts);
        yield return new("CAP_Core.Export.PdkResolution.PdkResolutionReport", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Import.ISParameterImporter", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Import.SParameterImportException", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Import.ImportedSParameters", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.UserPdkInfo", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.PdkTrashEntry", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.PdkTrashKind", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.PdkTrashRestoreResult", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.KeyedComponentSaveOutcome", RepositoryModules.Contracts);
        yield return new("CAP_DataAccess.Components.AddCustomComponent.KeyedComponentSaveResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.SavePromptResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ApplyResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ApplyAllResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.AgentBridge.Tools.AgentToolContent", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.AiTools.IAiTool", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.AiTools.IAiToolRegistry", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ComponentRegistry.RegistryArtifactChoice", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.ComponentRegistry.RegistryDownloadResult", RepositoryModules.Contracts);
        yield return new("CAP.Avalonia.Services.AgentBridge.Registration.CliRunResult", RepositoryModules.Contracts);
        // Presentation
        yield return new("CAP_Core.ErrorConsoleService", RepositoryModules.Presentation);
    }
}
