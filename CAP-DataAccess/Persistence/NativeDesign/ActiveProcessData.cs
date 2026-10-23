using System.Collections.Generic;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>
/// Serialisable form of the active process selection (issue #570).
/// </summary>
public class ActiveProcessData
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsPlayground { get; set; }
    public string? CoreMaterial { get; set; }
    public double? CoreThicknessNm { get; set; }
    public string? Cladding { get; set; }
    public int DesignWavelengthNm { get; set; } =
        CAP_Core.Components.Process.ProcessFingerprint.DefaultDesignWavelengthNm;
    public string? ProcessName { get; set; }
    public double? WidthToleranceNm { get; set; }
    public double? ThicknessToleranceNm { get; set; }
    public List<string> MemberPdkNames { get; set; } = new();
}
