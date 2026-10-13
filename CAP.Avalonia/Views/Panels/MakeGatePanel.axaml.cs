using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// The guided "Make gate" steps shown in the right panel's gate section while active.
/// All logic lives in <see cref="ViewModels.Analysis.LogicAnalysis.MakeGate.MakeGateViewModel"/>.
/// </summary>
public partial class MakeGatePanel : UserControl
{
    /// <summary>Initialises the panel.</summary>
    public MakeGatePanel()
    {
        InitializeComponent();
    }
}
