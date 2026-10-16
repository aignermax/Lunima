namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    /// <summary>Nonmodal first-start Python setup; absent in non-desktop/test hosts unless supplied.</summary>
    public Export.PythonEnvironmentManager.PythonStartupViewModel? PythonStartup { get; }
}
