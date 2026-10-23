namespace CAP_Core.Export.Contracts;

/// <summary>The package requirement displayed when the openEBL checker cannot find its toolchain.</summary>
public static class OpenEblToolchainRequirements
{
    public const string PipInstallCommand = "pip install klayout siepic_ebeam_pdk SiEPIC-Tools";
}
