using CAP.Avalonia.Services.GdsImport;
using CAP_DataAccess.Import.Gds;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Import.Gds;

namespace UnitTests.Architecture;

public sealed class GdsSourceReaderBoundaryTests
{
    [Fact]
    public async Task SourceCapability_ReusesTheConfiguredOwner()
    {
        await using var services = ProductionContainerTestHelper.BuildWithTempPreferences(
            Path.Combine(Path.GetTempPath(), $"lunima-gds-source-{Guid.NewGuid():N}.json"));
        services.GetRequiredService<IGdsSourceReader>().ShouldBeSameAs(services.GetRequiredService<GdsSourceReader>());
        services.GetRequiredService<GdsHierarchyExpansionService>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Analysis_UsesTheInjectedSourceAndPreservesSnapshotIdentity()
    {
        var source = new MemorySource();
        using var stop = new CancellationTokenSource();
        var analysis = await new GdsImportService(source).AnalyzeAsync("virtual.gds", stop.Token);
        source.Path.ShouldBe("virtual.gds");
        source.Token.ShouldBe(stop.Token);
        source.Parsed.ShouldBeSameAs(source.Bytes);
        analysis.SourceBytes.ShouldBeSameAs(source.Bytes);
        analysis.Library.ShouldBeSameAs(source.Library);
        analysis.TopCellCandidates.ShouldBe(new[] { "TOP" });
        analysis.SourceSha256.ShouldBe(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source.Bytes)));
    }

    [Fact]
    public async Task SharedSource_ParsesIndependentLibrariesAndRecoversAfterInvalidInput()
    {
        IGdsSourceReader source = new GdsSourceReader();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
            source.ParseAsync(GdsTestWriter.Create().StandardPrologue($"library-{i}")
                .BeginCell($"CELL-{i}").EndCell().EndLibrary().ToArray()))));
        for (var i = 0; i < results.Length; i++)
        {
            results[i].Name.ShouldBe($"library-{i}");
            results[i].Cells.Keys.ShouldBe(new[] { $"CELL-{i}" });
        }
        await Should.ThrowAsync<InvalidDataException>(() => source.ParseAsync([]));
        var valid = await source.ParseAsync(GdsTestWriter.Create().StandardPrologue("recovered")
            .BeginCell("TOP").EndCell().EndLibrary().ToArray());
        valid.Name.ShouldBe("recovered");
    }

    private sealed class MemorySource : IGdsSourceReader
    {
        internal byte[] Bytes { get; } = [1, 2, 3];
        internal GdsLibrary Library { get; } = CreateLibrary();
        internal string? Path { get; private set; }
        internal byte[]? Parsed { get; private set; }
        internal CancellationToken Token { get; private set; }
        public Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            Path = path;
            Token = cancellationToken;
            return Task.FromResult(Bytes);
        }
        public Task<GdsLibrary> ParseAsync(byte[] source, CancellationToken cancellationToken = default)
        {
            cancellationToken.ShouldBe(Token);
            Parsed = source;
            return Task.FromResult(Library);
        }
        private static GdsLibrary CreateLibrary()
        {
            var library = new GdsLibrary { Name = "virtual" };
            library.Cells.Add("TOP", new GdsCell { Name = "TOP" });
            return library;
        }
    }
}
