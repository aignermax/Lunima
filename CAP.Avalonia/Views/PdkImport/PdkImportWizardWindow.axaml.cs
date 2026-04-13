using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CAP.Avalonia.ViewModels.PdkImport;

namespace CAP.Avalonia.Views.PdkImport;

/// <summary>
/// Code-behind for the PDK Import Wizard dialog.
/// Wires up the file dialog for output path selection and auto-starts parsing on open.
/// </summary>
public partial class PdkImportWizardWindow : Window
{
    /// <summary>Initializes a new <see cref="PdkImportWizardWindow"/>.</summary>
    public PdkImportWizardWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not PdkImportWizardViewModel vm)
            return;

        // Wire up save dialog so the VM can show it without a UI reference
        vm.ShowSaveDialogAsync = async () =>
        {
            var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save PDK JSON",
                DefaultExtension = "json",
                SuggestedFileName = vm.PdkName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDK JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } },
                }
            });
            return result?.Path.LocalPath;
        };

        // Wire up close actions
        vm.OnCompleted = savedPath =>
        {
            Close(savedPath);
        };

        vm.OnCancelled = () =>
        {
            Close(null);
        };

        // Auto-start parsing when the dialog opens
        _ = vm.StartParsingAsync();
    }
}
