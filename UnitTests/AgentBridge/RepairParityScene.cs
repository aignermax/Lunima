using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing;
using UnitTests.Routing.CrossingInsertion.CrossingMove;

namespace UnitTests.AgentBridge;

/// <summary>The sealed counter exercises crowded repair; an actual four-port scene exercises crossing movement.</summary>
internal sealed class RepairParityScene(AgentDesignSession session, Action? disposeFileOperations = null) : IAsyncDisposable
{
    internal AgentDesignSession Session => session;
    internal DesignCanvasViewModel Canvas => session.Canvas;
    internal CommandManager Commands => session.Commands;
    internal string FirstBlockedId() => Canvas.Connections.First(view => view.Connection.IsBlockedFallback).Connection.Id.ToString();

    internal static async Task<RepairParityScene> CreateAsync(string family)
    {
        if (family != "move_crossing")
        {
            var bench = await RepairTestBench.LoadAsync(RepairTestBench.SealedCounterFixture);
            return new(bench.Session, bench.FileOps.Dispose);
        }
        var canvas = new DesignCanvasViewModel(new WaveguideRouter { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 });
        canvas.InitializeAStarRouting(0, 0, CrossedWiresScene.ChipSize, CrossedWiresScene.ChipSize);
        var scene = CrossedWiresScene.Build(canvas);
        var blocked = scene.West.RoutedPath!.DeepCopy();
        blocked.IsBlockedFallback = true;
        scene.West.RestoreCachedPath(blocked);
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        return new(new AgentDesignSession(canvas, commands, () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog())));
    }

    public async ValueTask DisposeAsync()
    {
        try { await Canvas.Document.RetireAsync(); }
        finally { Canvas.DisposeDocumentBindings(); disposeFileOperations?.Invoke(); }
    }
}
