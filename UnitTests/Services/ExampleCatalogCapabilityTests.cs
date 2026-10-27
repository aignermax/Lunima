using CAP.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace UnitTests.Services;

public sealed class ExampleCatalogCapabilityTests
{
    [Fact]
    public async Task ExamplePresentation_UsesTheConfiguredFileCatalogAndPreservesItsOrder()
    {
        var catalog = new Mock<IExampleFileCatalog>();
        catalog.Setup(value => value.GetExamples()).Returns(new[]
        {
            new ExampleFileEntry("Second by name", "virtual/first.lun", "example.first", "Basics"),
            new ExampleFileEntry("First by name", "virtual/second.lun", null, "Adders")
        });
        var services = new ServiceCollection();
        CAP.Avalonia.App.ConfigureServices(services);
        services.AddSingleton(catalog.Object);
        await using var provider = services.BuildServiceProvider();

        var examples = provider.GetRequiredService<ExampleDesignsService>().GetExamples();

        examples.Select(value => (value.Name, value.FilePath, value.DescriptionKey, value.Level))
            .ShouldBe(new[]
            {
                ("Second by name", "virtual/first.lun", "example.first", "Basics"),
                ("First by name", "virtual/second.lun", (string?)null, "Adders")
            });
        catalog.Verify(value => value.GetExamples(), Times.Once);
    }

    [Theory]
    [InlineData("virtual/examples")]
    [InlineData(null)]
    public async Task ExampleDirectory_UsesTheConfiguredCatalog(string? directory)
    {
        var catalog = new Mock<IExampleFileCatalog>();
        catalog.Setup(value => value.FindExamplesDirectory()).Returns(directory);
        var services = new ServiceCollection();
        CAP.Avalonia.App.ConfigureServices(services);
        services.AddSingleton(catalog.Object);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ExampleDesignsService>().FindExamplesDirectory().ShouldBe(directory);

        catalog.Verify(value => value.FindExamplesDirectory(), Times.Once);
    }
}
