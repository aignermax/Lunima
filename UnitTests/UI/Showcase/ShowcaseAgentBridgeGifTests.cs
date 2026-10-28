using System.Globalization;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.ViewModels;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase <c>agent-bridge.gif</c>: Claude repairing a design through the MCP agent
/// bridge. On the staged showcase chip the user drags the first probe pad 30 µm down, so its
/// metal trace to the phase shifter can no longer be routed (red). The REAL bridge tool
/// classes then run in-process against the MainWindow's canvas and undo history, in the
/// order an agent calls them: <c>list_connections</c> finds the blocked trace,
/// <c>suggest_fixes</c> tries its candidate nudges in a sandbox (none frees it on this chip),
/// and <c>move_component</c> lifts the pad clear, after which the trace routes again. The
/// terminal caption lists each call with a summary read from that tool's own JSON reply —
/// the agent's choice of calls is scripted, their results are not. Opt-in via <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseAgentBridgeGifTests
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const string PadTemplate = "Probe Pad";
    private const double PadDropUm = 30;
    private const int SuggestBudgetSeconds = 60;
    private const int MaxTrials = 6;
    private const int CleanMs = 700;
    private const int BrokenMs = 1200;
    private const int ListMs = 1300;
    private const int SuggestMs = 1500;
    private const int FinalHoldMs = 2600;

    /// <summary>World region (µm) around the phase shifter and its probe pads.</summary>
    private static readonly (double X, double Y, double W, double H) Focus = (260, -10, 800, 470);

    [AvaloniaFact]
    public async Task CaptureAgentBridgeRepair()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            await ShowcaseCircuit.BuildChipAsync(vm);
            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            ShowcaseAgentStage.HideRightPanel(window);
            ShowcaseCircuit.SetView(window, vm, Focus);
            var session = new AgentDesignSession(vm.Canvas, vm.CommandManager, () => vm.FileOperations,
                new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()), () => vm.LeftPanel.AllTemplates.ToList());
            var padId = vm.Canvas.Components.First(c => c.Name == PadTemplate).Component.Identifier;
            var gif = new ShowcaseGifRecorder("agent-bridge", window, ShowcaseAgentStage.CanvasWithToolbar(window));
            var caption = new ShowcaseTerminalCaption(window, "claude  ·  Lunima MCP agent bridge");
            gif.Capture(CleanMs);

            Body(await new MoveComponentTool(session).ExecuteAsync(Args($$"""{"id":"{{padId}}","dx":0,"dy":{{PadDropUm}}}""")));
            caption.Comment($"# you dragged {padId} {PadDropUm:0} µm down — its trace went red");
            gif.Capture(BrokenMs);

            var blocked = Body(await new ListConnectionsTool(session).ExecuteAsync(Args("""{"blocked_only":true}""")));
            blocked.GetProperty("count").GetInt32().ShouldBe(1, "the drag must leave exactly one trace unroutable");
            var wire = blocked.GetProperty("connections")[0];
            caption.Call("list_connections", "{blocked_only: true}");
            caption.Result($"1 blocked: {wire.GetProperty("start").GetString()} → {wire.GetProperty("end").GetString()}");
            gif.Capture(ListMs);

            var suggested = Body(await new SuggestFixesTool(session).ExecuteAsync(Args(
                $$"""{"connection_id":"{{wire.GetProperty("id").GetString()}}","time_budget_s":{{SuggestBudgetSeconds}},"max_trials":{{MaxTrials}}}""")));
            caption.Call("suggest_fixes", $"{{connection_id: \"{wire.GetProperty("id").GetString()![..8]}…\"}}");
            caption.Result(SummarizeSuggestions(suggested));
            gif.Capture(SuggestMs);

            var moved = Body(await new MoveComponentTool(session).ExecuteAsync(
                Args($$"""{"id":"{{padId}}","dx":0,"dy":{{-PadDropUm}}}""")));
            var routing = moved.GetProperty("routing");
            routing.GetProperty("blocked_count").GetInt32().ShouldBe(0, moved.ToString());
            caption.Call("move_component", $"{{id: \"{padId}\", dy: {(-PadDropUm).ToString(CultureInfo.InvariantCulture)}}}");
            caption.Result($"{routing.GetProperty("blocked_count").GetInt32()} of " +
                $"{routing.GetProperty("connection_count").GetInt32()} connections blocked — all routed");
            gif.Capture(FinalHoldMs);

            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>One line from <c>suggest_fixes</c>' own reply: how many candidates it tried
    /// and the best one's outcome.</summary>
    private static string SummarizeSuggestions(JsonElement body)
    {
        int tried = body.GetProperty("tried").GetInt32();
        var best = body.GetProperty("suggestions").EnumerateArray()
            .OrderBy(s => s.GetProperty("blocked_after").GetInt32()).FirstOrDefault();
        if (best.ValueKind == JsonValueKind.Undefined)
            return $"{tried} edits tried in a sandbox, none applicable";
        int before = body.GetProperty("blocked_before").GetInt32();
        int after = best.GetProperty("blocked_after").GetInt32();
        return after < before
            ? $"{tried} edits tried in a sandbox; best {best.GetProperty("id").GetString()}: {best.GetProperty("description").GetString()}"
            : $"{tried} nudges tried in a sandbox — none frees the trace";
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Body(AgentToolResult result)
    {
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }
}
