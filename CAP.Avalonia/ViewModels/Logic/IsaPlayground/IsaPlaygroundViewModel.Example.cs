using CAP.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

public partial class IsaPlaygroundViewModel
{
    /// <summary>Standard open-as-copy pipeline, including unsaved-design confirmation.</summary>
    public Func<string, Task<bool>>? OpenExampleRequested { get; set; }
    /// <summary>Builds the current canvas through the Logic panel.</summary>
    public Func<Task<bool>>? BuildLogicNetworkRequested { get; set; }
    internal Func<IReadOnlyList<ExampleDesign>> FindExamples { get; set; } = () => new ExampleDesignsService().GetExamples();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildExampleNetworkCommand))]
    private bool _hasOpenedSuitableExample;
    [ObservableProperty] private string _exampleActionStatus = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSuitableExampleCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildExampleNetworkCommand))]
    private bool _isExampleActionBusy;

    private bool CanOpenSuitableExample() => !IsExampleActionBusy;
    private bool CanBuildExampleNetwork() => HasOpenedSuitableExample && !IsExampleActionBusy;

    [RelayCommand(CanExecute = nameof(CanOpenSuitableExample))]
    private async Task OpenSuitableExample()
    {
        if (OpenExampleRequested == null) return;
        IsExampleActionBusy = true;
        ExampleActionStatus = "";
        try
        {
            var examples = FindExamples();
            var example = new[] { "Logic Gate ALU + RAM + ACC.lun", "Logic Gate 4-Bit Adder.lun" }
                .Select(name => examples.FirstOrDefault(e => string.Equals(Path.GetFileName(e.FilePath), name,
                    StringComparison.OrdinalIgnoreCase) && File.Exists(e.FilePath)))
                .FirstOrDefault(e => e != null);
            if (example == null)
            {
                ExampleActionStatus = Translate("IsaPlayground.ExampleMissing");
                return;
            }
            if (await OpenExampleRequested(example.FilePath))
            {
                StopRun();
                HasOpenedSuitableExample = true;
                ExampleActionStatus = Translate("IsaPlayground.ExampleReady");
            }
        }
        catch (Exception)
        {
            ExampleActionStatus = Translate("IsaPlayground.ExampleActionFailed");
        }
        finally { IsExampleActionBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanBuildExampleNetwork))]
    private async Task BuildExampleNetwork()
    {
        if (BuildLogicNetworkRequested == null) return;
        IsExampleActionBusy = true;
        try
        {
            StopRun();
            if (await BuildLogicNetworkRequested())
            {
                HasOpenedSuitableExample = false;
                ExampleActionStatus = "";
            }
            else ExampleActionStatus = Translate("IsaPlayground.ExampleBuildFailed");
        }
        catch (Exception)
        {
            ExampleActionStatus = Translate("IsaPlayground.ExampleBuildFailed");
        }
        finally { IsExampleActionBusy = false; }
    }
}
