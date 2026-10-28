using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP.Avalonia.Views;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.UI;

public sealed class ComponentSettingsCompositionTests
{
    [AvaloniaFact]
    public async Task DefaultWindow_OpensBasicSettings_AndEditsItsExistingSMatrix()
    {
        using var model = MainViewModelTestHelper.CreateMainViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        model.Canvas.AddComponent(component, "Compatibility instance");
        var instance = model.Canvas.Components.ShouldHaveSingleItem();
        var key = ComponentGeometryKey.For(component);
        model.FileOperations.StoredSMatrices[key] = new ComponentSMatrixData
        {
            SourceNote = "Existing import", Wavelengths = new()
            {
                ["1550"] = new()
                {
                    Rows = 2, Cols = 2, PortNames = ["in", "out"],
                    Real = [0, 1, 1, 0], Imag = [0, 0, 0, 0]
                }
            }
        };
        var owner = new MainWindow { DataContext = model };
        try
        {
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            model.Canvas.SelectedComponent = instance;
            model.CanvasInteraction.OpenSelectedComponentSettingsCommand.CanExecute(null).ShouldBeTrue();
            model.CanvasInteraction.OpenSelectedComponentSettingsCommand.Execute(null);
            var dialog = owner.OwnedWindows.OfType<ComponentSettingsDialog>().ShouldHaveSingleItem();
            dialog.IsVisible.ShouldBeTrue();
            var settings = dialog.DataContext.ShouldBeOfType<ComponentSettingsDialogViewModel>();
            settings.LoadFromFileCommand.CanExecute(null).ShouldBeTrue();
            var row = settings.SMatrixEntries.ShouldHaveSingleItem();
            row.SourceNote.ShouldBe("Existing import");
            row.PortNamesDisplay.ShouldBe("in, out");
            settings.DeleteEntryCommand.Execute(row);
            settings.SMatrixEntries.ShouldBeEmpty();
            model.FileOperations.StoredSMatrices.ContainsKey(key).ShouldBeFalse();
            model.CanvasInteraction.OpenSelectedComponentSettingsCommand.Execute(null);
            owner.OwnedWindows.OfType<ComponentSettingsDialog>().ShouldHaveSingleItem().ShouldBeSameAs(dialog);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.OfType<ComponentSettingsDialog>().ToArray())
            {
                await ((ComponentSettingsDialogViewModel)dialog.DataContext!).StopAsync();
                dialog.Close();
            }
            await model.StopAsync();
            model.FileOperations.HasUnsavedChanges = false;
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
