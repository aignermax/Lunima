using System.Xml.Linq;
using CAP.Avalonia.Services.Update;
using Shouldly;

namespace UnitTests.Update;

public class WindowsInstallerWorkflowTests
{
    [Fact]
    public void Msi_RecordsMachineInstallRootInRequiredFeature()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Installer", "Product.wxs")))
            root = root.Parent;
        root.ShouldNotBeNull();
        var doc = XDocument.Load(Path.Combine(root!.FullName, "Installer", "Product.wxs"));
        XNamespace ns = "http://wixtoolset.org/schemas/v4/wxs";
        var marker = doc.Descendants(ns + "RegistryValue")
            .Single(e => (string?)e.Attribute("Name") == "InstallRoot");
        ((string?)marker.Attribute("Root")).ShouldBe("HKLM");
        ((string?)marker.Attribute("Value")).ShouldBe("[INSTALLDIR]");
        ((string?)marker.Parent!.Attribute("Bitness")).ShouldBe("always64");
        doc.Descendants(ns + "Feature").Single(e => (string?)e.Attribute("Id") == "MainFeature")
            .Elements(ns + "ComponentRef").ShouldContain(e =>
                (string?)e.Attribute("Id") == (string?)marker.Parent.Attribute("Id"));
    }

    [Fact]
    public void WindowsUpdater_UsesElevatedBasicProgressWithoutRestart_AndGuardsExit()
    {
        var script = UpdaterScripts.BuildWindows(new InstallLocation(
            @"C:\Program Files\Lunima", @"C:\Program Files\Lunima\CAP.Desktop.exe", 99),
            @"C:\Users\O'Brien\My Downloads\update.msi");
        script.ShouldContain("-Verb RunAs");
        script.ShouldContain("/qb");
        script.ShouldContain("/norestart");
        script.ShouldContain("Get-Process -Id $targetPid");
        script.ShouldContain("3010");
        script.ShouldContain("O''Brien");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("other", false)]
    [InlineData("installed", true)]
    public void RegisteredRootMustMatchRunningApp(string? folder, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "installed");
        var registration = folder == null ? null : Path.Combine(Path.GetTempPath(), folder);
        var location = new InstallLocation(root, Path.Combine(root, "CAP.Desktop.exe"), 99);
        WindowsMsiRegistration.Matches(location, registration).ShouldBe(expected);
        WindowsMsiRegistration.Matches(new InstallLocation(root, Path.Combine(root, "testhost.exe"), 99), registration)
            .ShouldBeFalse();
    }
}
