using System.Collections.Immutable;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.CircuitOptimization;
using CAP_Core.Components.Core;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;

/// <summary>UI-only application targets for one result; never passed into the optimization worker.</summary>
internal sealed class OptimizationVariantBinding
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ImmutableArray<(ComponentViewModel View, Slider Slider, string Name)> _parameters;
    private DocumentRevisionStamp _revision;
    internal long Version { get; }

    internal OptimizationVariantBinding(DesignCanvasViewModel canvas, long version,
        IReadOnlyList<ComponentViewModel> components, IReadOnlyList<OptimizationParameter> parameters)
    {
        _canvas = canvas;
        Version = version;
        _revision = canvas.Document.CaptureRevision();
        _parameters = components.Select((component, index) =>
            (component, parameters[index].GetSlider(), parameters[index].DisplayName)).ToImmutableArray();
    }

    internal bool HasCurrentMembers(DesignCanvasViewModel canvas) => ReferenceEquals(canvas, _canvas)
        && _parameters.All(value => canvas.Components.Contains(value.View)
            && ReferenceEquals(value.View.Component.GetSlider(0), value.Slider));

    internal bool IsCurrent(DesignCanvasViewModel canvas, long version) => Version == version
        && HasCurrentMembers(canvas) && canvas.Document.IsCurrentRevision(_revision);

    internal void AdvanceAfterOwnApply() => _revision = _canvas.Document.CaptureRevision();

    internal ApplyOptimizationVariantCommand CreateCommand(OptimizationVariantViewModel variant)
    {
        if (variant.ParameterValues.Count != _parameters.Length)
            throw new InvalidOperationException("Optimization result does not match its captured parameter bindings.");
        return new(_parameters.Select((value, index) => (value.View, variant.ParameterValues[index])).ToArray(),
            $"variant #{variant.Rank}");
    }

    internal string Summary(OptimizationCandidate candidate) => string.Join("   ",
        candidate.ParameterValues.Select((value, index) => $"{_parameters[index].Name} = {Math.Round(value, 3)}"));
}
