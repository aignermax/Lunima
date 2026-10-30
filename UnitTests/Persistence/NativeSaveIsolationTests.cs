using Avalonia.Headless.XUnit;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Persistence;

public sealed class NativeSaveIsolationTests
{
    [Fact]
    public void MetadataCapture_DetachesMutableDesignRulesAndPdkVersions()
    {
        var original = new DesignMetadata
        {
            PdkVersions = new() { ["test-pdk"] = "v1" },
            DesignRules = new() { MinBendRadiusMicrometers = 40, MinSpacingMicrometers = 2, WaveguideWidthMicrometers = 0.5 }
        };
        var captured = NativeDesignCodec.BuildMetadataForSave(original);
        original.PdkVersions["test-pdk"] = "v2";
        original.DesignRules.MinBendRadiusMicrometers = 200;
        captured.PdkVersions["test-pdk"].ShouldBe("v1");
        captured.DesignRules!.MinBendRadiusMicrometers.ShouldBe(40);
    }

    [AvaloniaTheory]
    [InlineData("matrix")]
    [InlineData("group_pins")]
    [InlineData("group_outline")]
    public async Task AcceptedSave_OwnsValuesWhileEditingContinues(string change)
    {
        var files = new CapturingFiles();
        await using var main = MainViewModelTestHelper.CreateMainViewModel(designFiles: files);
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Identifier = "source";
        var points = new[] { new OutlinePoint(0, 0), new OutlinePoint(10, 0), new OutlinePoint(10, 10) };
        var group = new ComponentGroup("block")
        {
            OutlinePolygons = [new OutlinePolygon { Layer = 1, Points = points }],
            TruthTablePinAssignment = new() { InputPinNames = ["A"] }
        };
        group.AddChildren([component]);
        main.Canvas.AddComponent(group);
        var matrix = new ComponentSMatrixData
        {
            Wavelengths = new() { ["1550"] = new() { Rows = 1, Cols = 1, Real = [0.25], Imag = [0] } }
        };
        main.FileOperations.StoredSMatrices[component.Identifier] = matrix;
        var saving = main.FileOperations.SaveDesignToPathAsync("captured.lun");
        try
        {
            await files.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (change)
            {
                case "matrix": matrix.Wavelengths["1550"].Real[0] = 0.9; break;
                case "group_pins": group.TruthTablePinAssignment.InputPinNames[0] = "changed"; break;
                case "group_outline": points[0] = new OutlinePoint(300, 400); break;
            }
            files.Capture!.SMatrices![component.Identifier].Wavelengths["1550"].Real[0].ShouldBe(0.25);
            files.Capture.Groups!.Single().TruthTablePinAssignment!.InputPinNames.ShouldBe(["A"]);
            files.Capture.Groups.Single().GroupDto.BackgroundPolygons!.Single().Points[0].ShouldBe(new OutlinePoint(0, 0));
        }
        finally
        {
            files.Release.TrySetResult();
            (await saving.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        }
    }

    private sealed class CapturingFiles : INativeDesignFileStore
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DesignFileData? Capture { get; private set; }
        public bool Exists(string path) => false;
        public Task<string> ReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task WriteAsync(string path, DesignFileData data, CancellationToken cancellationToken = default)
        {
            Capture = data;
            Started.TrySetResult();
            await Release.Task;
        }
    }
}
