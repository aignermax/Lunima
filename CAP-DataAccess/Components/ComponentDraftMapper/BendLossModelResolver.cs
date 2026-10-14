using CAP_Core.LightCalculation.BendLoss;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Turns a PDK <see cref="ProcessDefinition"/> into the radius-dependent
    /// <see cref="BendLossModel"/> of its default (first) optical cross-section. An explicit
    /// <c>bendLoss</c> block on the cross-section wins field by field; otherwise the waveguide
    /// family is inferred from the process (core material, etch depth) and its literature-fitted
    /// preset applies. Mirrors <see cref="ProcessOpticalDefaultsResolver"/>: the same first
    /// optical cross-section defines the PDK's routing waveguide.
    /// </summary>
    public static class BendLossModelResolver
    {
        /// <summary>
        /// Minimum bend radii (µm) at or above which an InP cross-section is taken to be a
        /// shallow-etched rib: deep-etched InP bends at ~10 µm, shallow ribs need ≥ 100 µm.
        /// </summary>
        private const double InpShallowRibMinRadiusMicrometers = 50.0;

        private const double SoiCoreThicknessNm = 220.0;
        private const double SoiCoreThicknessToleranceNm = 30.0;

        /// <summary>
        /// The bend-loss model of <paramref name="process"/>, or null when there is no process
        /// (the connection then uses <see cref="BendLossPresets.Generic"/>).
        /// </summary>
        public static BendLossModel? Resolve(ProcessDefinition? process)
        {
            if (process == null)
                return null;

            var xsection = process.Xsections?.FirstOrDefault(x => x.Kind == XsectionKind.Optical);
            var draft = xsection?.BendLoss;
            var family = ParseFamily(draft?.Family) ?? InferFamily(process, xsection);
            var preset = BendLossPresets.ForFamily(family);
            return draft == null ? preset : ApplyOverrides(preset, draft);
        }

        /// <summary>
        /// Infers the waveguide family from the core material name (Si/SiN/InP), refined for InP
        /// by the cross-section's minimum radius or description; SOI is also recognised by a
        /// ~220 nm core thickness. Unknown platforms fall back to <see cref="WaveguideFamily.Generic"/>.
        /// </summary>
        public static WaveguideFamily InferFamily(ProcessDefinition process, ProcessXsection? xsection)
        {
            string core = CoreMaterialName(process);
            if (IsSiliconNitride(core))
                return WaveguideFamily.SiliconNitride;
            if (core.Contains("InP", StringComparison.OrdinalIgnoreCase) || core.Contains("InGaAs", StringComparison.OrdinalIgnoreCase))
                return IsShallowRib(xsection) ? WaveguideFamily.InpShallowRib : WaveguideFamily.InpDeepEtch;
            bool isSilicon = core == "Si" || string.Equals(core, "Silicon", StringComparison.OrdinalIgnoreCase);
            if (isSilicon || (core.Length == 0 && IsSoiThickness(process.CoreThicknessNm)))
                return WaveguideFamily.SoiStrip;
            return WaveguideFamily.Generic;
        }

        private static string CoreMaterialName(ProcessDefinition process) =>
            process.Materials?.FirstOrDefault(m => string.Equals(m.Role, "core", StringComparison.OrdinalIgnoreCase))?.Name?.Trim()
            ?? "";

        private static bool IsSiliconNitride(string core) =>
            core.Contains("nitride", StringComparison.OrdinalIgnoreCase)
            || (core.StartsWith("Si", StringComparison.Ordinal) && core.Contains('N'));

        private static bool IsShallowRib(ProcessXsection? xsection) =>
            xsection != null
            && (xsection.MinRadiusUm >= InpShallowRibMinRadiusMicrometers
                || (xsection.Description?.Contains("shallow", StringComparison.OrdinalIgnoreCase) ?? false));

        private static bool IsSoiThickness(double? thicknessNm) =>
            thicknessNm is double t && Math.Abs(t - SoiCoreThicknessNm) <= SoiCoreThicknessToleranceNm;

        private static WaveguideFamily? ParseFamily(string? name) =>
            Enum.TryParse<WaveguideFamily>(name, ignoreCase: true, out var family) && Enum.IsDefined(family)
                ? family
                : null;

        /// <summary>Applies every positive explicit coefficient of <paramref name="draft"/> onto the preset.</summary>
        private static BendLossModel ApplyOverrides(BendLossModel preset, ProcessBendLossDraft draft)
        {
            bool hasCoefficients = NonNegative(draft.RadiationLossDbPer90) != null
                || Positive(draft.RadiationDecayLengthUm) != null
                || NonNegative(draft.JunctionLossDb) != null;
            return preset with
            {
                Family = hasCoefficients ? WaveguideFamily.Custom : preset.Family,
                RadiationLossDbPer90AtReference = NonNegative(draft.RadiationLossDbPer90) ?? preset.RadiationLossDbPer90AtReference,
                RadiationReferenceRadiusMicrometers = Positive(draft.RadiationReferenceRadiusUm) ?? preset.RadiationReferenceRadiusMicrometers,
                RadiationDecayLengthMicrometers = Positive(draft.RadiationDecayLengthUm) ?? preset.RadiationDecayLengthMicrometers,
                CalibratedMinRadiusMicrometers = NonNegative(draft.CalibratedMinRadiusUm) ?? preset.CalibratedMinRadiusMicrometers,
                JunctionLossDbAtReference = NonNegative(draft.JunctionLossDb) ?? preset.JunctionLossDbAtReference,
                JunctionReferenceRadiusMicrometers = Positive(draft.JunctionReferenceRadiusUm) ?? preset.JunctionReferenceRadiusMicrometers,
                Source = string.IsNullOrWhiteSpace(draft.Source) ? preset.Source : draft.Source!,
            };
        }

        private static double? Positive(double? value) => value is double v && double.IsFinite(v) && v > 0 ? v : null;

        private static double? NonNegative(double? value) => value is double v && double.IsFinite(v) && v >= 0 ? v : null;
    }
}
