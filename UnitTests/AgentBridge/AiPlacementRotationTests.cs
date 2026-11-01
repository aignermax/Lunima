using System.IO;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Headless;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge;

/// <summary>The in-app assistant's <c>place_component</c> honours its rotation argument.</summary>
public class AiPlacementRotationTests
{
    [AvaloniaFact]
    public async Task PlaceComponent_AppliesTheRequestedRotation()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var grid = session.Services.GetRequiredService<IAiGridService>();

        foreach (var (rotation, expected, x) in new[] { (0, 0.0, 0.0), (90, 90.0, 400.0), (-90, 270.0, 800.0) })
        {
            var reply = await grid.PlaceComponentAsync("1x2 MMI Splitter", x, 500, rotation);

            reply.ShouldStartWith("Placed");
            session.Main.Canvas.Components[^1].Component.RotationDegrees.ShouldBe(expected);
        }
    }
}
