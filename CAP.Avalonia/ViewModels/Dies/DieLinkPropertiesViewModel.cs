using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Dies;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Dies;

/// <summary>
/// Properties of a selected die-to-die link (Properties panel): the facet gap (editing it
/// moves the far die along the link axis), the refractive index of the gap medium, the
/// lateral offset and the resulting coupling loss — the same model the simulation uses.
/// </summary>
public partial class DieLinkPropertiesViewModel : ObservableObject
{
    private const double DecibelsPerPowerDecade = 10.0;

    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commands;
    private bool _loading;

    /// <summary>Creates the view model.</summary>
    public DieLinkPropertiesViewModel(DesignCanvasViewModel canvas, CommandManager commands)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>Wavelength the loss readout is evaluated at.</summary>
    public double WavelengthNm { get; set; } = DieLinkGuideWavelengthNm;

    /// <summary>Default readout wavelength (standard C-band), matching the canvas guide.</summary>
    public const double DieLinkGuideWavelengthNm = 1550.0;

    [ObservableProperty] private WaveguideConnection? _link;
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private double _gapMicrometers;
    [ObservableProperty] private double _mediumIndex = WaveguideConnection.AirRefractiveIndex;
    [ObservableProperty] private string _lateralOffsetText = "";
    [ObservableProperty] private string _lossText = "";

    /// <summary>True while a die-to-die link is shown.</summary>
    public bool IsVisible => Link != null;

    /// <summary>Shows <paramref name="connection"/> when it is a die-to-die link, else hides the section.</summary>
    public void Show(WaveguideConnection? connection)
    {
        Link = connection is { IsCrossChipletFacetLink: true } ? connection : null;
        Reload();
    }

    /// <summary>Re-reads the link's geometry and loss.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            if (Link is { } link)
            {
                Label = $"{NameOf(link.StartPin)} ↔ {NameOf(link.EndPin)}";
                GapMicrometers = Math.Round(DieLinkGeometry.Gap(link), 3);
                MediumIndex = link.FacetGapMediumIndex;
                LateralOffsetText = string.Format(CultureInfo.CurrentCulture, "{0:0.00} µm", DieLinkGeometry.LateralOffset(link));
                LossText = FormatLoss(ChipletEdgeCouplerCoupling.FieldFactor(link, WavelengthNm));
            }
            OnPropertyChanged(nameof(IsVisible));
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnGapMicrometersChanged(double value) => Commit();

    partial void OnMediumIndexChanged(double value) => Commit();

    private void Commit()
    {
        if (_loading || Link == null)
            return;
        var command = EditDieLinkCommand.TryCreate(_canvas, Link, GapMicrometers, MediumIndex);
        if (command != null)
            _commands.ExecuteCommand(command);
        Reload();
    }

    private static string FormatLoss(double fieldFactor)
    {
        if (fieldFactor <= 0)
            return Services.Localization.LocalizationService.Instance.Translate("Workspace.System.NoCoupling");
        double lossDb = Math.Max(0.0, -2 * DecibelsPerPowerDecade * Math.Log10(fieldFactor));
        return string.Format(CultureInfo.CurrentCulture, "{0:0.00} dB", lossDb);
    }

    private static string NameOf(CAP_Core.Components.Core.PhysicalPin pin) =>
        DieAssembly.DieOf(pin.ParentComponent)?.GroupName ?? pin.ParentComponent.Identifier;
}
