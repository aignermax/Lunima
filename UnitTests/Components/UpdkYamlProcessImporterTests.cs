using System.IO;
using System.Linq;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Verifies the openEPDA uPDK YAML importer against a synthetic blueprint shaped
/// like a real one (header + xsections), NOT proprietary PDK data.
/// </summary>
public class UpdkYamlProcessImporterTests : IDisposable
{
    private readonly string _path;

    public UpdkYamlProcessImporterTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "updk-" + Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllText(_path,
            "blocks:\n" +
            "  Demo_BB:\n" +
            "    doc: a block\n" +
            "header:\n" +
            "  pdk_name: Demo_InP\n" +
            "  provider: DemoFab\n" +
            "  file_version: 1-2-3\n" +
            "xsections:\n" +
            "  E1700:\n" +
            "    width: 2.3\n" +
            "  ACT:\n" +
            "    width: 2.1\n" +
            "  GS_signal:\n" +
            "    width: 150.0\n");
    }

    [Fact]
    public void CanImport_AcceptsYamlOnly()
    {
        var importer = new UpdkYamlProcessImporter();
        importer.CanImport(_path).ShouldBeTrue();
        importer.CanImport("foo.csv").ShouldBeFalse();
    }

    [Fact]
    public void Import_ReadsHeaderAndXsectionWidths_ClassifyingOpticalVsMetal()
    {
        var process = new UpdkYamlProcessImporter().Import(_path);

        process.Name.ShouldBe("Demo_InP");
        process.Foundry.ShouldBe("DemoFab");
        process.Version.ShouldBe("1-2-3");

        process.Xsections.Count.ShouldBe(3);
        var e1700 = process.Xsections.First(x => x.Name == "E1700");
        e1700.WidthUm.ShouldBe(2.3);
        e1700.Kind.ShouldBe(XsectionKind.Optical);
        process.Xsections.First(x => x.Name == "ACT").Kind.ShouldBe(XsectionKind.Optical);
        process.Xsections.First(x => x.Name == "GS_signal").Kind.ShouldBe(XsectionKind.Metal);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best-effort */ }
    }
}
