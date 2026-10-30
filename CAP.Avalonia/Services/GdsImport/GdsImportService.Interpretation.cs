using System.Text.Json;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds.Interpretation;

namespace CAP.Avalonia.Services.GdsImport;

public sealed partial class GdsImportService
{
    /// <summary>Returns an independent copy of matching decisions saved in the open design.</summary>
    public GdsImportInterpretation? FindInterpretation(string sourceSha256, string topCell)
    {
        var saved = _designScope.Sets.Select(s => s.Interpretation).LastOrDefault(i => i is not null
            && i.TopCellName == topCell
            && string.Equals(i.SourceSha256, sourceSha256, StringComparison.OrdinalIgnoreCase));
        return saved is null ? null : JsonSerializer.Deserialize<GdsImportInterpretation>(JsonSerializer.Serialize(saved));
    }

    /// <summary>Lists explicit, persistable models available in the loaded component library.</summary>
    public IReadOnlyList<GdsModelCandidate> GetModelCandidates() =>
        (_templateProvider?.Invoke() ?? Array.Empty<ComponentTemplate>())
        .Where(t => t.SourceDraft?.SMatrix is not null)
        .Select(t => new GdsModelCandidate(t.PdkSource ?? string.Empty, t.Name,
            t.SourceDraft!.Pins.Select(p => p.Name).ToArray())
        {
            PinDomains = t.SourceDraft!.Pins.ToDictionary(p => p.Name, p => p.PinKind ?? "optical")
        }).ToArray();

    private static void ApplyReviewedModel(PdkComponentDraft draft, string cellName,
        GdsImportInterpretation interpretation, IReadOnlyList<ComponentTemplate> templates)
    {
        var decision = interpretation.Cells.FirstOrDefault(c => c.CellName == cellName);
        if (!string.IsNullOrWhiteSpace(decision?.DisplayName))
            draft.Name = GdsCellDraftMapper.SanitizeComponentName(decision.DisplayName);
        if (string.IsNullOrWhiteSpace(decision?.ModelComponentName))
        {
            // Unmapped: an explicit shape-only decision opts out of every assumed
            // model regardless of pin count. Without it, exactly two optical pins
            // keep the S-matrix null so the lossless pass-through default applies
            // — the same state the non-review import leaves. Any other pin set
            // cannot default its internal routing and stays deliberately
            // geometry-only.
            draft.ImportedModelStatus = decision?.GeometryOnly == true || !IsPassThroughEligible(draft)
                ? "geometry-only" : null;
            return;
        }
        var source = templates.FirstOrDefault(t => t.Name == decision.ModelComponentName
            && t.PdkSource == decision.ModelPdkName)?.SourceDraft;
        if (source?.SMatrix is null)
            throw new InvalidDataException($"Model '{decision.ModelPdkName}/{decision.ModelComponentName}' is unavailable or has no explicit S-matrix.");
        var importedNames = draft.Pins.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var sourceNames = source.Pins.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (decision.ModelPinMap.Keys.Any(n => !importedNames.Contains(n)))
            throw new InvalidDataException($"Cell '{cellName}' maps a port that does not exist.");
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pin in draft.Pins)
        {
            var target = decision.ModelPinMap.GetValueOrDefault(pin.Name, pin.Name);
            if (!sourceNames.Contains(target) || !reverse.TryAdd(target, pin.Name))
                throw new InvalidDataException($"Cell '{cellName}' needs a one-to-one mapping to every model port.");
            var modelPin = source.Pins.Single(p => p.Name == target);
            if (!string.Equals(pin.PinKind ?? "optical", modelPin.PinKind ?? "optical", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Port '{pin.Name}' and model port '{target}' have different signal domains.");
        }
        if (reverse.Count != sourceNames.Count)
            throw new InvalidDataException($"Cell '{cellName}' must map all model ports before simulation.");
        var model = JsonSerializer.Deserialize<PdkSMatrixDraft>(JsonSerializer.Serialize(source.SMatrix))!;
        foreach (var connection in model.Connections.Concat(
            model.WavelengthData?.SelectMany(w => w.Connections) ?? Enumerable.Empty<SMatrixConnection>()))
        {
            if (!reverse.TryGetValue(connection.FromPin, out var from) || !reverse.TryGetValue(connection.ToPin, out var to))
                throw new InvalidDataException($"Model for '{cellName}' references an undeclared port.");
            connection.FromPin = from;
            connection.ToPin = to;
        }
        draft.SMatrix = model;
        draft.Sliders = source.Sliders is null ? null :
            JsonSerializer.Deserialize<List<SliderDraft>>(JsonSerializer.Serialize(source.Sliders));
        draft.ImportedModelStatus = "mapped";
    }

    /// <summary>
    /// True when an unmodeled reviewed draft qualifies for the lossless 2-pin
    /// pass-through default (<see cref="PdkTemplateConverter"/>): exactly two
    /// pins, both optical.
    /// </summary>
    private static bool IsPassThroughEligible(PdkComponentDraft draft) =>
        IsPassThroughEligible(draft.Pins.Select(p => p.PinKind));

    /// <summary>
    /// The import's pass-through rule over raw pin-kind values, exposed so the
    /// review can warn about the assumed lossless model with the exact same
    /// decision the import makes — never a re-implemented copy.
    /// </summary>
    public static bool IsPassThroughEligible(IEnumerable<string?> pinKinds)
    {
        var kinds = pinKinds as IReadOnlyCollection<string?> ?? pinKinds.ToArray();
        return kinds.Count == 2 && kinds.All(kind =>
            PinKindHelper.TryParse(kind, out var matter) && matter == MatterType.Light);
    }
}
