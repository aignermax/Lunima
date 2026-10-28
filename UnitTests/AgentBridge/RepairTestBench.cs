using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.Integration;

namespace UnitTests.AgentBridge;

/// <summary>
/// A shipped example loaded through the production load path, wired like the app: the crossing
/// binder (adaptive insertion off, the default) with its undo journal on the same history the
/// agent session uses, and the full PDK library (so cuts find the crossing cell).
/// </summary>
internal sealed class RepairTestBench
{
    public const string CounterExample = "Logic Gate Counter 2-bit.lun";

    private RepairTestBench(AgentDesignSession session, FileOperationsViewModel fileOps)
    {
        Session = session;
        FileOps = fileOps;
    }

    public AgentDesignSession Session { get; }
    public FileOperationsViewModel FileOps { get; }
    public DesignCanvasViewModel Canvas => Session.Canvas;
    public CommandManager Commands => Session.Commands;

    /// <summary>
    /// An earlier bake of the counter with one wire sealed in by its neighbours (the shipped
    /// counter routes every wire now), for the repair tools to work on. Loaded from TestData.
    /// </summary>
    public const string SealedCounterFixture = "counter-sealed-pin.lun";

    public static async Task<RepairTestBench> LoadAsync(string example = CounterExample)
    {
        var (canvas, fileOps, _) = example == SealedCounterFixture
            ? await MziFringeAnalysis.LoadDesignFromPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TestData", example))
            : await MziFringeAnalysis.LoadExample(example);
        await fileOps.PostLoadRouting;
        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action())
        {
            IsEnabled = false,
        };
        var commands = new CommandManager();
        binder.UndoJournal.Attach(commands);
        canvas.CommandManager = commands;
        var session = new AgentDesignSession(canvas, commands, () => fileOps, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()), () => templates);
        return new RepairTestBench(session, fileOps);
    }

    public int BlockedCount() => Canvas.Connections.Count(c => c.Connection.IsBlockedFallback);

    public string FirstBlockedId() =>
        Canvas.Connections.First(c => c.Connection.IsBlockedFallback).Connection.Id.ToString();

    public static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static JsonElement Body(AgentToolResult result)
    {
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }
}
