using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for time-domain (transient) simulation via IFFT of S-parameters.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class TimeDomainPanel : UserControl
{
    /// <summary>Initializes the TimeDomainPanel.</summary>
    public TimeDomainPanel()
    {
        InitializeComponent();
    }
}
