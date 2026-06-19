using System.Collections.Generic;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Public, non-proprietary material defaults. Foundry refractive indices are
    /// under NDA and never shipped — these silicon-photonics values (publicly
    /// known) seed a new process and act as the FDTD fall-back when the user has
    /// not entered foundry-specific indices (the result is then flagged as
    /// uncalibrated rather than silently wrong).
    /// </summary>
    public static class ProcessMaterialDefaults
    {
        /// <summary>Silicon-on-insulator starter materials (public n values).</summary>
        public static List<ProcessMaterial> Soi() => new()
        {
            new ProcessMaterial
            {
                Name = "Si",
                Role = "core",
                NByWavelengthNm = new Dictionary<int, double> { [1310] = 3.504, [1550] = 3.476 },
            },
            new ProcessMaterial
            {
                Name = "SiO2",
                Role = "cladding",
                NByWavelengthNm = new Dictionary<int, double> { [1310] = 1.447, [1550] = 1.444 },
            },
        };
    }
}
