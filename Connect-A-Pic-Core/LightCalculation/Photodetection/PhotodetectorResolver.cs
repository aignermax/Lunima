namespace CAP_Core.LightCalculation.Photodetection;

/// <summary>Resolves explicitly selected absolute receivers independently of legacy normalized simulations.</summary>
public static class PhotodetectorResolver
{
    /// <summary>Compact-model responsivity override key, in A/W.</summary>
    public const string ResponsivityKey = "responsivityAmpsPerWatt";
    /// <summary>Historical normalized-model responsivity in A/W.</summary>
    public const double LegacyResponsivityAmpsPerWatt = 0.8;

    /// <summary>
    /// Generic Ge-on-Si estimate: R rounded from 0.95 A/W, dark current 6 nA,
    /// from Germanium wrap-around photodetectors on Silicon photonics (2015),
    /// https://e3s-center.berkeley.edu/wp-content/uploads/2019/06/2015_Germanium-Wrap-Around-Photodetectors-on-Silicon-Photonics.pdf.
    /// Sensitivity is a rounded -20 dBm system assumption inspired by Joo et al.,
    /// https://doi.org/10.1364/OE.18.016474 (-18.5 dBm at BER 1e-12, 10 Gb/s).
    /// This composite estimate is neither one measured device nor foundry-qualified data.
    /// </summary>
    public static PhotodetectorDescription Generic { get; } = new(1, 6, -20, 10e9, 1e-12, 1550,
        "generic", "Ge wrap-around PD (2015): R rounded from 0.95 A/W, dark current 6 nA; " +
        "Joo et al., doi:10.1364/OE.18.016474: -18.5 dBm at 10 Gb/s, BER 1e-12; " +
        "-20 dBm is an illustrative receiver assumption, not a measured specification.");

    /// <summary>Process values win over generic defaults; an explicit compact-model R wins over both.</summary>
    public static PhotodetectorDescription Resolve(PhotodetectorDescription? processDetector = null,
        IReadOnlyDictionary<string, double>? compactModelParameters = null)
    {
        var detector = processDetector ?? Generic;
        if (compactModelParameters == null || !compactModelParameters.TryGetValue(ResponsivityKey, out var value))
            return detector;
        return new(value, detector.DarkCurrentNa, detector.SensitivityDbm, detector.BitRateHz,
            detector.Ber, detector.WavelengthNm, "custom",
            $"Explicit compactModel {ResponsivityKey}; other assumptions: {detector.Provenance}: {detector.Source}");
    }
}
