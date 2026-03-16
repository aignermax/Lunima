using CAP_Core.Components;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for PdkInfo model class.
/// </summary>
public class PdkInfoTests
{
    [Fact]
    public void PdkInfo_Constructor_SetsPropertiesCorrectly()
    {
        var pdkInfo = new PdkInfo("Test PDK", "/path/to/pdk.json", true, 10);

        pdkInfo.Name.ShouldBe("Test PDK");
        pdkInfo.FilePath.ShouldBe("/path/to/pdk.json");
        pdkInfo.IsBundled.ShouldBeTrue();
        pdkInfo.ComponentCount.ShouldBe(10);
        pdkInfo.IsEnabled.ShouldBeTrue(); // Default enabled
    }

    [Fact]
    public void PdkInfo_BuiltInComponents_HasNullFilePath()
    {
        var pdkInfo = new PdkInfo("Built-in", null, true, 5);

        pdkInfo.FilePath.ShouldBeNull();
        pdkInfo.IsBundled.ShouldBeTrue();
    }

    [Fact]
    public void PdkInfo_UserLoaded_HasFilePath()
    {
        var pdkInfo = new PdkInfo("User PDK", "/user/custom.json", false, 20);

        pdkInfo.FilePath.ShouldBe("/user/custom.json");
        pdkInfo.IsBundled.ShouldBeFalse();
    }

    [Fact]
    public void PdkInfo_UpdateComponentCount_ChangesCount()
    {
        var pdkInfo = new PdkInfo("PDK", null, true, 5);

        pdkInfo.UpdateComponentCount(15);

        pdkInfo.ComponentCount.ShouldBe(15);
    }

    [Fact]
    public void PdkInfo_SourceType_ReturnsBundledForBundledPdk()
    {
        var pdkInfo = new PdkInfo("Bundled PDK", "/bundled/pdk.json", true, 10);

        pdkInfo.SourceType.ShouldBe("Bundled");
    }

    [Fact]
    public void PdkInfo_SourceType_ReturnsUserForUserPdk()
    {
        var pdkInfo = new PdkInfo("User PDK", "/user/pdk.json", false, 10);

        pdkInfo.SourceType.ShouldBe("User");
    }

    [Fact]
    public void PdkInfo_IsEnabled_CanBeToggled()
    {
        var pdkInfo = new PdkInfo("PDK", null, true, 5);

        pdkInfo.IsEnabled.ShouldBeTrue();

        pdkInfo.IsEnabled = false;
        pdkInfo.IsEnabled.ShouldBeFalse();

        pdkInfo.IsEnabled = true;
        pdkInfo.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void PdkInfo_ToString_ReturnsFormattedString()
    {
        var pdkInfo = new PdkInfo("Demo PDK", "/demo.json", true, 25);

        var result = pdkInfo.ToString();

        result.ShouldBe("Demo PDK (25 components, Bundled)");
    }
}
