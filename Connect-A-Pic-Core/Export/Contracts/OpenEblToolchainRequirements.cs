namespace CAP_Core.Export.Contracts;

/// <summary>
/// The PyPI distributions the vendored openEBL check scripts import, and the install
/// command shown when the checker cannot find them. Every name must resolve on PyPI:
/// pip aborts the whole command on one unknown requirement and installs nothing.
/// </summary>
public static class OpenEblToolchainRequirements
{
    /// <summary>PyPI distribution that provides <c>import klayout.db</c>.</summary>
    public const string KlayoutPackage = "klayout";

    /// <summary>PyPI distribution that provides <c>import siepic_ebeam_pdk</c>.</summary>
    public const string EBeamPdkPackage = "siepic_ebeam_pdk";

    /// <summary>
    /// PyPI distribution that provides <c>import SiEPIC</c>. The upstream project is
    /// called SiEPIC-Tools, but no PyPI package carries that name.
    /// </summary>
    public const string SiepicPackage = "SiEPIC";

    /// <summary>Command that installs every package the openEBL checks import.</summary>
    public const string PipInstallCommand =
        "pip install " + KlayoutPackage + " " + EBeamPdkPackage + " " + SiepicPackage;

    /// <summary>Message reported when no interpreter can import the toolchain.</summary>
    public const string ToolchainMissingMessage =
        "The openEBL check needs a Python toolchain with the packages " + KlayoutPackage + ", " +
        EBeamPdkPackage + " and " + SiepicPackage + ". Install them with: " + PipInstallCommand;

    /// <summary>The packages of <see cref="PipInstallCommand"/>, in command order.</summary>
    public static IReadOnlyList<string> PipPackages { get; } =
        new[] { KlayoutPackage, EBeamPdkPackage, SiepicPackage };
}
