using System.Text.Json;
using System.Text.Json.Serialization;
using CAP_DataAccess.Persistence.NativeDesign;
using Shouldly;

namespace UnitTests.Persistence;

public sealed class NativeDesignFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lunima native save ä " + Guid.NewGuid().ToString("N"));
    public NativeDesignFileStoreTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_PreservesExistingJsonFormatAndRoundTrips(bool existing)
    {
        var path = Path.Combine(_directory, "chip.lun");
        if (existing) await File.WriteAllTextAsync(path, "original");
        var data = new DesignFileData
        {
            FormatVersion = "2.0", Workspace = "Logic", ChipWidthMicrometers = 1234.5,
            Components = [new() { Identifier = "input", TemplateName = "straight", X = 12, Y = 34 }]
        };
        var expected = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var files = new NativeDesignFileStore();
        await files.WriteAsync(path, data);
        var json = await files.ReadAsync(path);
        json.ShouldBe(expected);
        NativeDesignCodec.Parse(json)!.Components.Single().Identifier.ShouldBe("input");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact]
    public async Task LateSerializationFailure_PreservesPreviouslySavedDocument()
    {
        var path = Path.Combine(_directory, "chip.lun");
        await File.WriteAllTextAsync(path, "original");
        var data = new DesignFileData
        {
            Components = Enumerable.Range(0, 5000).Select(index => new ComponentData
                { Identifier = "component-" + index, TemplateName = "straight", X = index }).ToList(),
            ChipWidthMicrometers = double.NaN
        };
        await Should.ThrowAsync<ArgumentException>(() => new NativeDesignFileStore().WriteAsync(path, data));
        (await File.ReadAllTextAsync(path)).ShouldBe("original");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
