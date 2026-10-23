using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export.Contracts;
using CAP_Core.Export.PythonEnvironmentManager;
using Moq;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

public class PythonEnvironmentRegistryPortTests
{
    [Fact]
    public async Task SelectingManagedInterpreter_UpdatesRegistryAndRebuildsActiveRow()
    {
        var environment = new PythonEnvironment { Name = "contract-env", VenvPath = "contract-venv" };
        PythonEnvironment? active = null;
        var registry = new Mock<IPythonEnvironmentRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.GetAll()).Returns(new[] { environment });
        registry.Setup(x => x.GetActive()).Returns(() => active);
        registry.Setup(x => x.SetActive(environment.Name)).Callback(() => active = environment);
        var vm = new PythonEnvironmentManagerViewModel(registry.Object,
            new UvBootstrapper(), new NazcaPackageInstaller(),
            new EnvironmentHealthChecker(new CAP_Core.Export.PythonDiscoveryService()),
            Mock.Of<IPythonDiscovery>(), provisioner: Mock.Of<IManagedPythonProvisioner>());
        try
        {
            vm.Interpreters.Single().IsActive.ShouldBeFalse();

            vm.SetActiveInterpreterCommand.Execute(vm.Interpreters.Single());

            active.ShouldBeSameAs(environment);
            vm.Interpreters.Single().IsActive.ShouldBeTrue();
            vm.Interpreters.Single().ManagedName.ShouldBe(environment.Name);
            registry.Verify(x => x.SetActive(environment.Name), Times.Once);
            registry.Verify(x => x.SetActiveExternalPath(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            await vm.StopAsync();
        }
    }
}
