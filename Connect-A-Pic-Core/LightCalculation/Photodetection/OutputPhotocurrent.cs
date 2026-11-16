namespace CAP_Core.LightCalculation.Photodetection;

/// <summary>Absolute signal photocurrent for an output pin, including a listen-only coupler.</summary>
public sealed record OutputPhotocurrent(Guid OutputPinId, PhotodetectorDescription Detector,
    double? ReceivedPowerWatts, double? PhotocurrentAmps)
{
    private const double WattsPerMilliwatt = 1e-3;
    private const double DecibelsPerDecade = 10;

    /// <summary>Returns no current when absolute power is unknown; never interprets normalized power as watts.</summary>
    public static OutputPhotocurrent FromWatts(Guid outputPinId, PhotodetectorDescription detector, double? powerWatts)
    {
        ArgumentNullException.ThrowIfNull(detector);
        double? current = powerWatts is { } power ? detector.PhotocurrentAmps(power) : null;
        return new(outputPinId, detector, powerWatts, current);
    }

    /// <summary>Converts dBm relative to 1 mW into signal photocurrent, excluding dark current.</summary>
    public static OutputPhotocurrent FromDbm(Guid outputPinId, PhotodetectorDescription detector, double powerDbm)
    {
        PhotodetectorDescription.Finite(powerDbm, nameof(powerDbm));
        var watts = WattsPerMilliwatt * Math.Pow(10, powerDbm / DecibelsPerDecade);
        PhotodetectorDescription.Positive(watts, nameof(powerDbm));
        return FromWatts(outputPinId, detector, watts);
    }
}
