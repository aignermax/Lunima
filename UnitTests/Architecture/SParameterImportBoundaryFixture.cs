using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP_Core.Export;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import;
using Moq;

namespace UnitTests.Architecture;

internal static class SParameterImportBoundaryFixture
{
    internal static ImportedSParameters Model() => new()
    {
        SourceFormat = "Configured parser", SourceFilePath = "original.sparam", PortCount = 2,
        PortNames = ["port 1", "port 2"], Metadata = new() { ["mode"] = "TE" },
        SMatricesByWavelengthNm = new()
        {
            [1550] = new Complex[,]
            {
                { new(.1, .2), new(.3, .4) },
                { new(.5, .6), new(.7, .8) }
            }
        }
    };

    internal static IFileDialogService Picker(string path)
    {
        var picker = new Mock<IFileDialogService>();
        picker.Setup(value => value.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(path);
        return picker.Object;
    }

    internal static NewComponentViewModel Editor(ISParameterImportCatalog catalog)
    {
        var renderer = new Mock<IPdkGeometryRenderer>();
        renderer.Setup(value => value.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NazcaPreviewResult
            {
                Success = true, XMin = 0, YMin = 0, XMax = 8, YMax = 2,
                Pins = [new() { Name = "in", X = 0, Y = 1, Angle = 180 },
                    new() { Name = "out", X = 8, Y = 1, Angle = 0 }]
            });
        var store = new Mock<IUserPdkLibrary>();
        store.Setup(value => value.ListCustomPdks()).Returns(Array.Empty<UserPdkInfo>());
        return new NewComponentViewModel(catalog, new ComponentGeometryExtractor(renderer.Object, renderer.Object),
            null, store.Object, Array.Empty<ProcessDefinition>())
        {
            SelectedBackend = GeometryBackend.GdsFactory,
            Code = "component = configured_geometry()",
            PickSMatrixFile = () => Task.FromResult<string?>("virtual.configured")
        };
    }

    internal sealed class HeldImporter : ISParameterImporter
    {
        public IReadOnlyList<string> SupportedExtensions => [".configured", ".sparam"];
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ImportedSParameters> Finish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? Path { get; private set; }
        public Task<ImportedSParameters> ImportAsync(string filePath)
        {
            Path = filePath;
            Entered.TrySetResult();
            return Finish.Task;
        }
    }

    internal sealed class Catalog(ISParameterImporter importer) : ISParameterImportCatalog
    {
        internal List<(string Path, SParameterImportDetection Detection)> Requests { get; } = [];
        public Task<ISParameterImporter?> FindAsync(string path, SParameterImportDetection detection,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((path, detection));
            return Task.FromResult<ISParameterImporter?>(importer);
        }
    }
}
