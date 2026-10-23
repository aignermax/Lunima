using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Analysis.OnaAnalysis;
using Moq;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

internal sealed class OnaExportFixture : IAsyncDisposable
{
    public DesignCanvasViewModel Canvas { get; } = new();
    public ErrorConsoleService Errors { get; } = new();
    public SweepLifetimeFixture.ControlledSweep Runner { get; } = new();
    public Mock<IFileDialogService> Files { get; } = new();
    public ExportModel Model { get; }

    public OnaExportFixture()
    {
        var source = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        source.IsUserMarkedLightSource = true;
        source.LaserEnabled = true;
        Canvas.Components.Add(new ComponentViewModel(source));
        Runner.Barrier.Release.TrySetResult();
        Model = new ExportModel(Errors, Runner) { FileDialogService = Files.Object };
        Model.Configure(Canvas);
        Files.Setup(files => files.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("measurement.csv");
    }

    public void HoldPicker(RetirementBarrier picker) =>
        Files.Setup(files => files.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () => { await picker.WaitAsync(CancellationToken.None); return "measurement.csv"; });

    public async ValueTask DisposeAsync()
    {
        Model.Writer.Release.TrySetResult();
        await Model.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Canvas.DisposeDocumentBindings();
    }

    internal sealed class ExportModel(ErrorConsoleService errors, IWavelengthSweepRunner runner)
        : OnaSweepViewModel(errors, runner)
    {
        public RetirementBarrier Writer { get; } = new();
        public int Writes { get; private set; }
        public string? Content { get; private set; }
        public bool FailWrite { get; set; }

        protected override async Task WriteCsvAsync(string path, string content, CancellationToken cancellationToken)
        {
            Writes++;
            Content = content;
            await Writer.WaitAsync(cancellationToken);
            if (FailWrite) throw new IOException("Controlled late CSV failure");
        }
    }
}
