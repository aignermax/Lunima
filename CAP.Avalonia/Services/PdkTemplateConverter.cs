using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using System.Numerics;

namespace CAP.Avalonia.Services;

/// <summary>
/// Converts PDK component definitions from JSON (PdkComponentDraft) into
/// ComponentTemplate instances ready for placement on the canvas.
/// </summary>
public static class PdkTemplateConverter
{
    /// <summary>
    /// Converts a <see cref="PdkComponentDraft"/> into a <see cref="ComponentTemplate"/>.
    /// </summary>
    /// <param name="pdkComp">The PDK component draft from the loaded JSON file.</param>
    /// <param name="pdkName">Display name of the PDK (becomes PdkSource on the template).</param>
    /// <param name="nazcaModuleName">Optional Python module name for Nazca import generation.</param>
    /// <returns>A fully configured <see cref="ComponentTemplate"/> with S-Matrix factory.</returns>
    public static ComponentTemplate ConvertToTemplate(
        PdkComponentDraft pdkComp,
        string pdkName,
        string? nazcaModuleName)
    {
        var pinDefs = pdkComp.Pins.Select(p => new PinDefinition(
            p.Name,
            p.OffsetXMicrometers,
            p.OffsetYMicrometers,
            p.AngleDegrees
        )).ToArray();

        // NazcaOriginOffset is required — validated by PdkLoader.
        double nazcaOriginOffsetX = pdkComp.NazcaOriginOffsetX ?? 0;
        double nazcaOriginOffsetY = pdkComp.NazcaOriginOffsetY ?? 0;

        var template = new ComponentTemplate
        {
            Name = pdkComp.Name,
            Category = pdkComp.Category,
            WidthMicrometers = pdkComp.WidthMicrometers,
            HeightMicrometers = pdkComp.HeightMicrometers,
            PinDefinitions = pinDefs,
            NazcaFunctionName = pdkComp.NazcaFunction,
            NazcaParameters = pdkComp.NazcaParameters,
            HasSlider = pdkComp.Sliders?.Any() ?? false,
            SliderMin = pdkComp.Sliders?.FirstOrDefault()?.MinVal ?? 0,
            SliderMax = pdkComp.Sliders?.FirstOrDefault()?.MaxVal ?? 100,
            PdkSource = pdkName,
            NazcaModuleName = nazcaModuleName,
            NazcaOriginOffsetX = nazcaOriginOffsetX,
            NazcaOriginOffsetY = nazcaOriginOffsetY,
        };

        if (pdkComp.SMatrix?.WavelengthData is { Count: > 0 } wlData)
        {
            template.CreateWavelengthSMatrixMap = pins =>
            {
                var map = new Dictionary<int, SMatrix>();
                foreach (var entry in wlData)
                {
                    var draft = new PdkSMatrixDraft
                    {
                        WavelengthNm = entry.WavelengthNm,
                        Connections = entry.Connections
                    };
                    map[entry.WavelengthNm] = CreateSMatrixFromPdk(pins, draft);
                }
                return map;
            };
        }
        else
        {
            template.CreateSMatrix = pins => CreateSMatrixFromPdk(pins, pdkComp.SMatrix);
        }

        return template;
    }

    /// <summary>
    /// Builds an <see cref="SMatrix"/> from a <see cref="PdkSMatrixDraft"/>.
    /// Each JSON connection entry creates both forward and reverse transfers.
    /// </summary>
    public static SMatrix CreateSMatrixFromPdk(List<Pin> pins, PdkSMatrixDraft? sMatrixDraft)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new List<(Guid, double)>());

        if (sMatrixDraft?.Connections == null || sMatrixDraft.Connections.Count == 0)
            return sMatrix;

        var pinByName = new Dictionary<string, Pin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in pins)
            pinByName[pin.Name] = pin;

        var transfers = new Dictionary<(Guid, Guid), Complex>();

        foreach (var conn in sMatrixDraft.Connections)
        {
            if (!pinByName.TryGetValue(conn.FromPin, out var fromPin) ||
                !pinByName.TryGetValue(conn.ToPin, out var toPin))
                continue;

            var phaseRad = conn.PhaseDegrees * Math.PI / 180.0;
            var value = Complex.FromPolarCoordinates(conn.Magnitude, phaseRad);

            transfers[(fromPin.IDInFlow, toPin.IDOutFlow)] = value;
            transfers[(toPin.IDInFlow, fromPin.IDOutFlow)] = value;
        }

        sMatrix.SetValues(transfers);
        return sMatrix;
    }
}
