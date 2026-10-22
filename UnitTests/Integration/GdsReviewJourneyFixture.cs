using System.Diagnostics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using UnitTests.Import.Gds;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Shared fixture for <see cref="GdsReviewJourneyTests"/> (issue #1566): drives the
/// six-step interpretation review of a small GDS (one top cell, two instances of a
/// 2-optical-port waveguide child, one non-port layer) purely through the review
/// ViewModel's commands, applies it, routes the two cells' facing pins through the
/// normal routing path, and saves/reloads the design. Every review-stage observation
/// is captured in a property so each journey step is asserted by its own test.
/// </summary>
public sealed class GdsReviewJourneyFixture : IAsyncLifetime
{
    /// <summary>Position tolerance for pin-placement assertions (µm), per issue #1566.</summary>
    public const double Tolerance = 0.01;

    /// <summary>Name given to the occurrence group created during the review.</summary>
    public const string GroupName = "chain";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gds-review-journey-" + Guid.NewGuid().ToString("N"));

    /// <summary>Temp directory holding the fixture GDS and the saved design.</summary>
    public string Root => _root;

    /// <summary>Design scope host backing the import canvas.</summary>
    public GdsDesignScopeTestHost Host { get; } = new();

    /// <summary>Canvas the reviewed import is placed on.</summary>
    public DesignCanvasViewModel Canvas { get; } = new();

    /// <summary>The import dialog whose review was driven through its commands.</summary>
    public GdsImportDialogViewModel Dialog { get; private set; } = null!;

    /// <summary>Layers with their confirmed roles, captured at the Layers stage.</summary>
    public IReadOnlyList<(int Layer, int Datatype, GdsLayerRole Role)> LayersSeen { get; private set; } = [];

    /// <summary>Importable cells exposed at the Cells stage.</summary>
    public IReadOnlyList<string> CellsSeen { get; private set; } = [];

    /// <summary>Number of placement occurrences listed at the Cells stage.</summary>
    public int OccurrenceCount { get; private set; }

    /// <summary>The group created from the two selected occurrences at the Cells stage.</summary>
    public GdsInterpretationGroup? GroupCaptured { get; private set; }

    /// <summary>Reviewed ports of the child cell, captured at the Ports stage.</summary>
    public IReadOnlyList<(string Name, double XUm, double YUm)> PortsSeen { get; private set; } = [];

    /// <summary>True when the Model stage was left at its default (no model mapped).</summary>
    public bool ModelLeftAtDefault { get; private set; }

    /// <summary><c>CanApply</c> observed at the Summary stage.</summary>
    public bool CanApplyAtSummary { get; private set; }

    /// <summary>SHA-256 of the imported source bytes (for interpretation lookup after load).</summary>
    public string SourceSha256 { get; private set; } = "";

    /// <summary>The component group the reviewed import placed on the canvas.</summary>
    public ComponentGroup Group { get; private set; } = null!;

    /// <summary>Left imported cell (GDS origin instance).</summary>
    public Component CellA { get; private set; } = null!;

    /// <summary>Right imported cell (instance at GDS x = 20 µm).</summary>
    public Component CellB { get; private set; } = null!;

    /// <summary>The routed connection between the facing pins.</summary>
    public WaveguideConnectionViewModel Connection { get; private set; } = null!;

    /// <summary>Path of the saved .lun design.</summary>
    public string SavedPath { get; private set; } = "";

    /// <summary>Fresh design scope host the saved design was reloaded into.</summary>
    public GdsDesignScopeTestHost LoadHost { get; private set; } = null!;

    /// <summary>Fresh canvas the saved design was reloaded into.</summary>
    public DesignCanvasViewModel LoadCanvas { get; private set; } = null!;

    /// <summary>The group as reloaded from the .lun file.</summary>
    public ComponentGroup LoadedGroup { get; private set; } = null!;

    /// <summary>Reloaded left cell.</summary>
    public Component LoadedCellA { get; private set; } = null!;

    /// <summary>Reloaded right cell.</summary>
    public Component LoadedCellB { get; private set; } = null!;

    /// <summary>The reviewed interpretation found via <c>FindInterpretation</c> after reload.</summary>
    public GdsImportInterpretation? LoadedInterpretation { get; private set; }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var gdsPath = WriteGds();
        Canvas.InitializeAStarRouting(-200, -100, 200, 100);
        var executor = new GdsPlacementExecutor(Canvas, new CommandManager(), () => Host.Templates.ToList());
        Dialog = new GdsImportDialogViewModel(gdsPath, Host.CreateService(), executor)
        {
            EnableReview = true,
            GroupImportRequested = false,
            ReviewProfileStore = new GdsLayerProfileStore(Path.Combine(_root, "review-profiles.json")),
        };
        await Dialog.StartAnalysisAsync();
        await WaitUntil(() => Dialog.Review != null && !Dialog.IsBusy);
        Dialog.HasError.ShouldBeFalse(Dialog.ErrorText);
        var review = Dialog.Review!;

        await DriveReviewAsync(review);
        await ApplyAndImportAsync(review);
        CapturePlacement();
        await RouteFacingPins();
        await SaveAndReload();
    }

    /// <inheritdoc/>
    public Task DisposeAsync()
    {
        Dialog.OnWindowClosed();
        Host.Dispose();
        LoadHost.Dispose();
        if (Directory.Exists(_root) && Environment.GetEnvironmentVariable("KEEP_GDS_REVIEW_JOURNEY") == null)
            Directory.Delete(_root, true);
        return Task.CompletedTask;
    }

    /// <summary>Steps through stages 0–5 via <c>NextCommand</c>, capturing each stage.</summary>
    private async Task DriveReviewAsync(GdsInterpretationReviewViewModel review)
    {
        review.StageIndex.ShouldBe(0, review.Message);
        await review.NextCommand.ExecuteAsync(null);

        review.StageIndex.ShouldBe(1, review.Message);
        foreach (var layer in review.Layers)
        {
            layer.Role = layer.Layer == 1 && layer.Datatype == 0 ? GdsLayerRole.OpticalCore
                : layer.Layer == 1 && layer.Datatype == 10 ? GdsLayerRole.PortLabel
                : GdsLayerRole.Auxiliary;
        }
        LayersSeen = review.Layers.Select(l => (l.Layer, l.Datatype, l.Role)).ToList();
        await review.NextCommand.ExecuteAsync(null);

        review.StageIndex.ShouldBe(2, review.Message);
        CellsSeen = review.ActiveCells.Select(c => c.CellName).ToList();
        var occurrences = review.Occurrences.ToArray();
        OccurrenceCount = occurrences.Length;
        if (occurrences.Length == 2)
        {
            review.PickPlacement([new(occurrences[0].CellName, occurrences[0].Transform)], false);
            review.PickPlacement([new(occurrences[1].CellName, occurrences[1].Transform)], true);
            review.GroupName = GroupName;
            review.CreateOccurrenceGroupCommand.Execute(null);
            GroupCaptured = review.BuildInterpretation().Groups.SingleOrDefault();
        }
        await review.NextCommand.ExecuteAsync(null);

        review.StageIndex.ShouldBe(3, review.Message);
        PortsSeen = review.Cells.Single(c => c.CellName == "wg").Ports
            .Select(p => (p.Name, p.XUm, p.YUm)).ToList();
        await review.NextCommand.ExecuteAsync(null);

        review.StageIndex.ShouldBe(4, review.Message);
        ModelLeftAtDefault = review.Cells.Single(c => c.CellName == "wg").SelectedModel is null;
        await review.NextCommand.ExecuteAsync(null);

        review.StageIndex.ShouldBe(5, review.Message);
        CanApplyAtSummary = review.CanApply;
        SourceSha256 = review.BuildInterpretation().SourceSha256;
    }

    /// <summary>Presses Apply (NextCommand at the Summary stage) and awaits the import.</summary>
    private async Task ApplyAndImportAsync(GdsInterpretationReviewViewModel review)
    {
        Dialog.OnClose = () => Dialog.OnWindowClosed();
        await review.NextCommand.ExecuteAsync(null);
        await WaitUntil(() => Dialog.ImportCompleted || Dialog.HasError);
        Dialog.HasError.ShouldBeFalse(Dialog.ErrorText);
        Dialog.ImportCompleted.ShouldBeTrue("the confirmed review must import");
    }

    private void CapturePlacement()
    {
        Group = Canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        var ordered = Group.ChildComponents.OrderBy(c => c.PhysicalX).ToList();
        CellA = ordered[0];
        CellB = ordered[1];
    }

    private async Task RouteFacingPins()
    {
        var aOut = CellA.PhysicalPins.Single(p => p.Name == "out");
        var bIn = CellB.PhysicalPins.Single(p => p.Name == "in");
        Connection = Canvas.ConnectPins(aOut, bIn)
            .ShouldNotBeNull("the facing pins must be connectable");
        await Canvas.RecalculateRoutesAsync();
    }

    private async Task SaveAndReload()
    {
        SavedPath = Path.Combine(_root, "journey.lun");
        await GdsImportJourneyFixture.SaveToFile(
            GdsImportJourneyFixture.CreateFileOperations(Canvas, Host), SavedPath);
        LoadHost = new GdsDesignScopeTestHost();
        LoadCanvas = new DesignCanvasViewModel();
        await GdsImportJourneyFixture.LoadFromFile(
            GdsImportJourneyFixture.CreateFileOperations(LoadCanvas, LoadHost), SavedPath);
        LoadedGroup = LoadCanvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        var ordered = LoadedGroup.ChildComponents.OrderBy(c => c.PhysicalX).ToList();
        LoadedCellA = ordered[0];
        LoadedCellB = ordered[1];
        LoadedInterpretation = LoadHost.CreateService().FindInterpretation(SourceSha256, "TOP");
    }

    /// <summary>
    /// TOP with two instances of one 10×4 µm gdsfactory-style waveguide cell
    /// (core stripe on (1,0), extent frame on the non-port layer (111,0), in/out
    /// port labels on (1,10)), 20 µm apart so their facing pins have a 10 µm gap.
    /// </summary>
    private string WriteGds()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "review-journey.gds");
        var content = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wg", 0, 0)
                .SRef("wg", 20000, 0)
            .EndCell()
            .BeginCell("wg")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "in", 0, 2000)
                .Text(1, 10, "out", 10000, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray();
        File.WriteAllBytes(path, content);
        return path;
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate() && elapsed.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        predicate().ShouldBeTrue("the dialog must finish its asynchronous work");
    }
}
