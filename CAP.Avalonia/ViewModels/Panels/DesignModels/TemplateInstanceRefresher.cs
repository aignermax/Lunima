using System;
using CAP_Core;
using System.Collections.Generic;
using System.Linq;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP.Avalonia.ViewModels.Library;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP.Avalonia.ViewModels.Panels.DesignModels;

/// <summary>
/// Type-wide propagation of a saved component definition: placed instances snapshot their
/// S-matrix at placement time, so an editor save would otherwise only affect FUTURE placements.
/// Pushes the saved template's matrices into every matching live instance; explicit overrides
/// keep winning.
/// </summary>
public sealed class TemplateInstanceRefresher
{
    private readonly ComponentTemplateResolver _templates;
    private readonly DesignModelOverrides _overrides;
    private readonly ErrorConsoleService? _errorConsole;

    /// <summary>Creates the template-to-instance model propagation workflow.</summary>
    public TemplateInstanceRefresher(ComponentTemplateResolver templates, DesignModelOverrides overrides,
        ErrorConsoleService? errorConsole)
    {
        _templates = templates;
        _overrides = overrides;
        _errorConsole = errorConsole;
    }

    /// <summary>
    /// Rebuilds the base S-matrix map of every placed instance of <paramref name="template"/>
    /// (including instances inside groups), re-applies overrides on top, and invalidates the
    /// caller's simulation when it reports matching instances.
    /// </summary>
    public bool Refresh(IEnumerable<Component> components, ComponentTemplate template)
    {
        // The save path treats names case-insensitively; the instance match must too, or a
        // case-only rename silently skips every placed instance.
        var templateKey = $"{template.PdkSource}::{template.Name}";
        var matching = FlattenGroupChildren(components)
            .Where(c => string.Equals(_templates.Key(c), templateKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matching.Count == 0)
            return false;

        foreach (var component in matching)
            RebuildBaseSMatrices(component, template);

        _overrides.Apply(matching);
        return true;
    }

    private static IEnumerable<Component> FlattenGroupChildren(IEnumerable<Component> components)
    {
        foreach (var component in components)
        {
            if (component is ComponentGroup group)
            {
                foreach (var child in FlattenGroupChildren(group.ChildComponents))
                    yield return child;
            }
            else
            {
                yield return component;
            }
        }
    }

    private void RebuildBaseSMatrices(Component component, ComponentTemplate template)
    {
        var logicalPins = component.PhysicalPins
            .Where(pp => pp.LogicalPin != null)
            .Select(pp => pp.LogicalPin!)
            .ToList();
        if (logicalPins.Count == 0)
            return;

        // A pin rename/count change means the saved definition no longer describes this live
        // instance; rebuilding would silently produce half-populated matrices. Keep its physics.
        if (!TemplatePinsMatchInstance(template, logicalPins))
        {
            _errorConsole?.LogWarning(
                $"Template pins changed — placed instance '{component.Identifier}' keeps its previous " +
                $"S-matrix; replace it to adopt the new definition of '{template.Name}'.");
            return;
        }

        try
        {
            var map = BuildWavelengthMap(template, logicalPins, component.GetAllSliders());
            if (map != null)
                component.WaveLengthToSMatrixMap = map;
        }
        catch (Exception ex)
        {
            // Keep current matrices rather than guessing — no silent physics.
            _errorConsole?.LogWarning(
                $"Could not refresh the S-matrix of '{component.Identifier}' from the saved " +
                $"definition of '{template.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// True when the template's pin definitions AND every pin name in its stored S-matrix data
    /// resolve one-to-one against the live instance's pins.
    /// </summary>
    private static bool TemplatePinsMatchInstance(ComponentTemplate template, List<Pin> instancePins)
    {
        var instanceNames = new HashSet<string>(
            instancePins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var templateNames = new HashSet<string>(
            template.PinDefinitions.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        return templateNames.SetEquals(instanceNames)
            && DraftConnectionsResolveAgainst(template.SourceDraft?.SMatrix, instanceNames);
    }

    private static bool DraftConnectionsResolveAgainst(PdkSMatrixDraft? draft, HashSet<string> pinNames)
    {
        if (draft is null)
            return true;
        var connections = (draft.Connections ?? new List<SMatrixConnection>())
            .Concat(draft.WavelengthData?.SelectMany(e => e.Connections)
                    ?? Enumerable.Empty<SMatrixConnection>());
        return connections.All(c => pinNames.Contains(c.FromPin) && pinNames.Contains(c.ToPin));
    }

    /// <summary>
    /// Builds the wavelength map like placement does, but against the live instance's existing
    /// logical pins so the pin GUIDs the simulator routes by stay valid.
    /// </summary>
    private static Dictionary<int, SMatrix>? BuildWavelengthMap(
        ComponentTemplate template, List<Pin> pins, List<Slider> sliders)
    {
        if (template.CreateWavelengthSMatrixMap != null)
            return template.CreateWavelengthSMatrixMap(pins);

        SMatrix single;
        if (template.CreateSMatrixWithSliders != null)
            single = template.CreateSMatrixWithSliders(pins, sliders);
        else if (template.CreateSMatrix != null)
            single = template.CreateSMatrix(pins);
        else
            return null;

        return new Dictionary<int, SMatrix> { { 1550, single }, { 1310, single }, { 980, single } };
    }

}
