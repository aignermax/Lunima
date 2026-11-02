using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP_Core.Export.FoundryDrc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary><c>check_foundry_drc</c> over a stub DRC runner: deck listing and marker-to-component mapping.</summary>
public class AgentFoundryDrcToolTests
{
    private sealed class StubRunner : IFoundryDrcRunner
    {
        public IReadOnlyDictionary<string, string> ShippedDecks() =>
            new Dictionary<string, string> { ["foundry_deck"] = "/decks/foundry_deck.lydrc" };

        // One marker centred on canvas (140, 225): GDS is y-up, so y = -225 there.
        public Task<FoundryDrcResult> RunAsync(string gdsPath, string deckPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FoundryDrcResult(true, null,
                new[] { new DrcRuleSummary("min_space", "Gap", 1), new DrcRuleSummary("min_width", "Width", 0) },
                new[] { new DrcMarker("min_space", "TOP", 130, -230, 150, -220) }));
    }

    private static async Task<JsonElement> Call(HeadlessAgentSession session, string tool, string args)
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    [AvaloniaFact]
    public async Task Markers_AreMappedToTheComponentTheyLieOn()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null,
            services => services.AddSingleton<IFoundryDrcRunner>(new StubRunner()));
        await Call(session, "new_design", "{}");
        var placed = await Call(session, "place_component", """{"type":"1x2 MMI Splitter","x":100,"y":200,"name":"split"}""");

        (await Call(session, "check_foundry_drc", "{}")).GetProperty("decks")[0].GetString().ShouldBe("foundry_deck");
        var report = await Call(session, "check_foundry_drc", """{"deck":"foundry_deck","gds_path":"/tmp/any.gds"}""");

        report.GetProperty("clean").GetBoolean().ShouldBeFalse();
        report.GetProperty("rules")[0].GetProperty("rule").GetString().ShouldBe("min_space", "worst rule first");
        var marker = report.GetProperty("markers")[0];
        marker.GetProperty("y").GetDouble().ShouldBe(225);
        marker.GetProperty("component").GetString().ShouldBe(placed.GetProperty("placed").GetProperty("id").GetString());
    }
}
