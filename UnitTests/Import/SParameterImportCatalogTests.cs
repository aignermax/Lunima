using CAP_DataAccess.Import;
using Shouldly;

namespace UnitTests.Import;

public sealed class SParameterImportCatalogTests
{
    [Theory]
    [InlineData("('port 1','TE',1,'port 1',1,'transmission')", true)]
    [InlineData("1.93414e+14 0 0 1 0 1 0 0 0", true)]
    [InlineData("unrelated text document", false)]
    [InlineData("1.93414e+14 1 0", false)]
    [InlineData("", false)]
    public async Task TextDetection_PreservesExplicitPerConsumerPolicy(string contents, bool identified)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunima-sparam-probe-{Guid.NewGuid():N}.TXT");
        var lumerical = new LumericalSParameterImporter();
        var touchstone = new TouchstoneImporter();
        var catalog = new SParameterImportCatalog([lumerical, touchstone]);
        try
        {
            await File.WriteAllTextAsync(path, "# comment\n! comment\n\n  " + contents);
            (await catalog.FindAsync(path, SParameterImportDetection.ExtensionOnly)).ShouldBeSameAs(lumerical);
            var detected = await catalog.FindAsync(path, SParameterImportDetection.VerifyAmbiguousText);
            if (identified) detected.ShouldBeSameAs(lumerical);
            else detected.ShouldBeNull();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Selection_UsesConfiguredOrder_AndOwnsTheCollection()
    {
        var first = new LumericalSParameterImporter();
        var second = new LumericalSParameterImporter();
        var configured = new List<ISParameterImporter> { first, second };
        var catalog = new SParameterImportCatalog(configured);
        configured.Clear();
        (await catalog.FindAsync("virtual.DAT", SParameterImportDetection.VerifyAmbiguousText))
            .ShouldBeSameAs(first);
        (await catalog.FindAsync("virtual.unknown", SParameterImportDetection.ExtensionOnly)).ShouldBeNull();
    }

    [Fact]
    public async Task PreCancelledDetection_RejectsBeforeOpeningAFile()
    {
        var catalog = new SParameterImportCatalog([new LumericalSParameterImporter()]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            catalog.FindAsync("nonexistent.txt", SParameterImportDetection.VerifyAmbiguousText, cancellation.Token));
    }
}
