using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels.DesignModels;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Maps native design component state.</summary>
public sealed class NativeComponentMapper
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentTemplateResolver _templates;

    /// <summary>Creates the component mapping adapter for the current canvas.</summary>
    public NativeComponentMapper(DesignCanvasViewModel canvas, ComponentTemplateResolver templates)
    {
        _canvas = canvas;
        _templates = templates;
    }

    /// <summary>
    /// Creates a ComponentData DTO from a ComponentViewModel.
    /// Uses FindTemplateName to resolve the correct library template name,
    /// including components ungrouped from UserGroup templates (which have no TemplateName on the VM).
    /// </summary>
    public ComponentData CreateComponentData(ComponentViewModel c)
    {
        return new ComponentData
        {
            TemplateName = _templates.Name(c.Component),
            PdkSource = c.TemplatePdkSource ?? _templates.PdkSource(c.Component),
            X = c.X,
            Y = c.Y,
            Identifier = c.Component.Identifier,
            Rotation = (int)c.Component.Rotation90CounterClock,
            RotationDegrees = ComponentPoseTransform.GetNonCardinalRotationDegrees(c.Component),
            Mirrored = c.Component.IsMirroredHorizontally ? true : null,
            IsBackground = c.Component.IsRoutingObstacle ? null : true,
            SliderValue = c.HasSliders ? c.SliderValue : null,
            SliderValues = NativeComponentStateMapper.SnapshotSliderValues(c.Component),
            LaserWavelengthNm = c.LaserConfig?.WavelengthNm,
            LaserPower = c.LaserConfig?.InputPower,
            LaserEnabled = c.LaserConfig?.IsEnabled == false ? false : null,
            LaserLineShape = c.LaserConfig?.IsSpectralShape == true
                ? c.LaserConfig.LineShape.ToString() : null,
            LaserLinewidthFwhmNm = c.LaserConfig?.LinewidthFwhmNm,
            LaserRinDbPerHz = c.LaserConfig?.RinDbPerHz,
            IsLocked = c.Component.IsLocked ? true : null,
            IsInsertedCrossing = c.Component.IsInsertedCrossing ? true : null,
            HumanReadableName = c.Component.HumanReadableName
        };
    }

    /// <summary>
    /// Loads a single component from saved data and adds it to the canvas.
    /// </summary>
    public ComponentViewModel? LoadComponentFromData(ComponentData compData)
    {
        var template = _templates.Find(compData.TemplateName, compData.PdkSource);

        if (template == null)
            return null;

        var component = ComponentTemplates.CreateFromTemplate(template, compData.X, compData.Y);

        // Restore identifier to preserve references
        component.Identifier = compData.Identifier;
        ComponentTemplates.ReserveIdentifier(compData.Identifier);

        // Restore HumanReadableName
        if (compData.HumanReadableName != null)
            component.HumanReadableName = compData.HumanReadableName;

        NativeComponentStateMapper.RestorePose(component, compData.Mirrored, compData.Rotation, compData.RotationDegrees);
        // Before AddComponent: a background component must never register as an obstacle.
        if (compData.IsBackground == true)
            component.IsRoutingObstacle = false;

        var vm = _canvas.AddComponent(component, template.Name, template.PdkSource);

        // Restore slider values (all sliders; legacy single value as fallback)
        NativeComponentStateMapper.RestoreSliderValues(vm.Component, compData.SliderValues, compData.SliderValue);

        // Restore laser configuration
        if (vm.LaserConfig != null)
        {
            if (compData.LaserWavelengthNm.HasValue)
                vm.LaserConfig.WavelengthNm = compData.LaserWavelengthNm.Value;
            if (compData.LaserPower.HasValue)
                vm.LaserConfig.InputPower = compData.LaserPower.Value;
            if (compData.LaserEnabled.HasValue)
                vm.LaserConfig.IsEnabled = compData.LaserEnabled.Value;
            if (compData.LaserLineShape != null
                && Enum.TryParse<CAP_Core.ExternalPorts.LaserSpectrum.LaserLineShape>(
                    compData.LaserLineShape, out var lineShape))
                vm.LaserConfig.LineShape = lineShape;
            if (compData.LaserLinewidthFwhmNm.HasValue)
                vm.LaserConfig.LinewidthFwhmNm = compData.LaserLinewidthFwhmNm.Value;
            if (compData.LaserRinDbPerHz.HasValue)
                vm.LaserConfig.RinDbPerHz = compData.LaserRinDbPerHz.Value;
        }

        // Restore lock state
        if (compData.IsLocked == true)
            component.IsLocked = true;

        // Restore the auto-inserted-crossing marker so its dissolution record
        // can be rebuilt once all connections are loaded (#705)
        if (compData.IsInsertedCrossing == true)
            component.IsInsertedCrossing = true;

        return vm;
    }
}
