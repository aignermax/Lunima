using System.Text.Json.Serialization;

namespace CAP_Core.LightCalculation.Photodetection;

/// <summary>Immutable detector and receiver assumptions at a stated wavelength, BER and bit rate.</summary>
public sealed class PhotodetectorDescription
{
    /// <summary>Creates a validated description; foundry provenance must name its qualification source.</summary>
    [JsonConstructor]
    public PhotodetectorDescription(double responsivityAmpsPerWatt = double.NaN, double darkCurrentNa = double.NaN,
        double sensitivityDbm = double.NaN, double bitRateHz = double.NaN, double ber = double.NaN,
        double wavelengthNm = double.NaN, string provenance = "", string source = "")
    {
        Positive(responsivityAmpsPerWatt, nameof(responsivityAmpsPerWatt));
        Nonnegative(darkCurrentNa, nameof(darkCurrentNa));
        Finite(sensitivityDbm, nameof(sensitivityDbm));
        Positive(bitRateHz, nameof(bitRateHz));
        Positive(ber, nameof(ber));
        if (ber >= 1) throw new ArgumentOutOfRangeException(nameof(ber));
        Positive(wavelengthNm, nameof(wavelengthNm));
        if (provenance is not ("generic" or "literature" or "foundry" or "custom"))
            throw new ArgumentException("Provenance must be generic, literature, foundry or custom.", nameof(provenance));
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ResponsivityAmpsPerWatt = responsivityAmpsPerWatt;
        DarkCurrentNa = darkCurrentNa;
        SensitivityDbm = sensitivityDbm;
        BitRateHz = bitRateHz;
        Ber = ber;
        WavelengthNm = wavelengthNm;
        Provenance = provenance;
        Source = source;
    }

    /// <summary>Photodiode responsivity in A/W.</summary>
    [JsonPropertyName("responsivityAmpsPerWatt")]
    public double ResponsivityAmpsPerWatt { get; }
    /// <summary>Dark current in nA; excluded from signal photocurrent.</summary>
    [JsonPropertyName("darkCurrentNa")]
    public double DarkCurrentNa { get; }
    /// <summary>Receiver/system sensitivity in dBm, not a universal diode constant.</summary>
    [JsonPropertyName("sensitivityDbm")]
    public double SensitivityDbm { get; }
    /// <summary>Bit rate in bits/s at which sensitivity is assumed.</summary>
    [JsonPropertyName("bitRateHz")]
    public double BitRateHz { get; }
    /// <summary>Target bit error ratio at the stated sensitivity.</summary>
    [JsonPropertyName("ber")]
    public double Ber { get; }
    /// <summary>Reference optical wavelength in nm.</summary>
    [JsonPropertyName("wavelengthNm")]
    public double WavelengthNm { get; }
    /// <summary>Explicit data origin; JSON presence alone never implies foundry qualification.</summary>
    [JsonPropertyName("provenance")]
    public string Provenance { get; }
    /// <summary>Literature citation or foundry qualification document and operating conditions.</summary>
    [JsonPropertyName("source")]
    public string Source { get; }

    /// <summary>Signal current in A for finite, nonnegative absolute optical power in W.</summary>
    public double PhotocurrentAmps(double powerWatts)
    {
        Nonnegative(powerWatts, nameof(powerWatts));
        var current = ResponsivityAmpsPerWatt * powerWatts;
        Finite(current, nameof(powerWatts));
        return current;
    }

    internal static void Finite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name, "Value must be finite.");
    }

    internal static void Positive(double value, string name)
    {
        Finite(value, name);
        if (value <= 0) throw new ArgumentOutOfRangeException(name, "Value must be positive.");
    }

    internal static void Nonnegative(double value, string name)
    {
        Finite(value, name);
        if (value < 0) throw new ArgumentOutOfRangeException(name, "Value must be nonnegative.");
    }
}
