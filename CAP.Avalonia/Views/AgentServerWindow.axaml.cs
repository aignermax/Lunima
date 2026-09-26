using Avalonia.Controls;

namespace CAP.Avalonia.Views;

/// <summary>
/// Non-modal window for the agent live-state server: start/stop the
/// localhost endpoint and copy the MCP client configuration.
/// </summary>
public partial class AgentServerWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public AgentServerWindow()
    {
        InitializeComponent();
    }
}
