using Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Solvers;
using CAP_Core.Components.Creation;
using CAP_Core.Preferences;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;

namespace UnitTests.ViewModels;

public class FeaturePreferencePortTests
{
    [Fact]
    public async Task LeftPanel_ImportsPdkAndPersistsWidthThroughIndependentStores()
    {
        var canvas = new DesignCanvasViewModel();
        try
        {
            var library = new Mock<IGroupLibrary>();
            library.SetupGet(x => x.UserTemplates).Returns(Array.Empty<GroupTemplate>());
            library.SetupGet(x => x.PdkTemplates).Returns(Array.Empty<GroupTemplate>());
            var reader = new Mock<IPdkReader>();
            const string path = "contract-only-pdk.json";
            reader.Setup(x => x.LoadFromFile(path)).Returns(new PdkDraft { Name = "ContractFoundry" });
            var files = new Mock<IFileDialogService>();
            files.Setup(x => x.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(path);
            var pdkPreferences = new PdkPreferences();
            var layout = new Mock<IPanelLayoutPreferences>(MockBehavior.Strict);
            layout.Setup(x => x.SetLeftPanelWidth(365));
            var panel = new LeftPanelViewModel(canvas, library.Object, reader.Object,
                pdkPreferences, layout.Object, new HierarchyPanelViewModel(canvas),
                new PdkManagerViewModel(), new ComponentLibraryViewModel(library.Object))
            {
                FileDialogService = files.Object
            };

            panel.LeftPanelWidth = new GridLength(365);
            await panel.LoadPdkCommand.ExecuteAsync(null);

            layout.Verify(x => x.SetLeftPanelWidth(365), Times.Once);
            pdkPreferences.Paths.ShouldBe(new[] { path });
            panel.PdkManager.IsPdkNameLoaded("ContractFoundry", null).ShouldBeTrue();
            reader.Verify(x => x.LoadFromFile(path), Times.Once);
        }
        finally
        {
            canvas.DisposeDocumentBindings();
        }
    }

    [Fact]
    public void CloudCredentialEdits_PersistThroughFdtdPortWithoutChangingBackend()
    {
        var preferences = new Mock<IFdtdPreferences>(MockBehavior.Strict);
        preferences.Setup(x => x.GetTidy3dApiKey()).Returns("original-key");
        preferences.Setup(x => x.SetTidy3dApiKey("new-key"));
        var settings = new Tidy3dSettingsViewModel(preferences.Object);

        settings.ApiKey.ShouldBe("original-key");
        settings.ApiKey = "new-key";

        settings.IsApiKeySet.ShouldBeTrue();
        preferences.Verify(x => x.SetTidy3dApiKey("new-key"), Times.Once);
        preferences.Verify(x => x.SetFdtdBackend(It.IsAny<FdtdBackendType>()), Times.Never);
    }

    private sealed class PdkPreferences : IPdkLibraryPreferences
    {
        public List<string> Paths { get; } = new();
        public HashSet<string> GetEnabledPdks() => new();
        public HashSet<string> GetKnownPdks() => new();
        public void SetPdkFilterState(IEnumerable<string> enabledPdkNames, IEnumerable<string> knownPdkNames) { }
        public List<string> GetUserPdkPaths() => Paths.ToList();
        public void AddUserPdkPath(string filePath) => Paths.Add(filePath);
        public void RemoveUserPdkPath(string filePath) => Paths.Remove(filePath);
    }
}
