using CAP.Avalonia.Services.Solvers;
using CAP_Core.Export;
using CAP_Core.Solvers.Protocol;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers;

public sealed class SolverApplicationBoundaryTests
{
    [Fact]
    public void RequestFactory_RequiresRendererContractsRatherThanProcessImplementations()
    {
        var constructor = typeof(ComponentFdtdRequestFactory).GetConstructors().Single();
        constructor.GetParameters().Take(2).Select(parameter => parameter.ParameterType)
            .ShouldAllBe(type => type == typeof(IPdkGeometryRenderer));
    }

    [Theory]
    [InlineData(typeof(NazcaComponentPreviewService))]
    [InlineData(typeof(GdsFactoryComponentPreviewService))]
    public void ConfiguredBackend_ImplementsTheApplicationRenderingBoundary(Type backend) =>
        typeof(IPdkGeometryRenderer).IsAssignableFrom(backend).ShouldBeTrue();

    [Theory]
    [InlineData("", null)]
    [InlineData("diagnostic chatter\n{bad}\n", null)]
    [InlineData("chatter\r\n  {\"success\":true}  \r\n", "{\"success\":true}")]
    [InlineData("{\"first\":1}\n{\"last\":2}\n", "{\"last\":2}")]
    [InlineData("{\"valid\":true}\n{broken}\n", "{\"valid\":true}")]
    [InlineData("{\"valid\":true}\n[1,2]\nnull\n42\n", "{\"valid\":true}")]
    [InlineData("log\n{\"nested\":{\"text\":\"brace }\"}}\n", "{\"nested\":{\"text\":\"brace }\"}}")]
    public void TrailingJson_CharacterizesTheExistingSingleLineObjectProtocol(string stdout, string? expected)
    {
        SubprocessJsonRunner.ExtractTrailingJsonLine(stdout).ShouldBe(expected);
        TrailingJsonObject.Extract(stdout).ShouldBe(expected);
    }
}
