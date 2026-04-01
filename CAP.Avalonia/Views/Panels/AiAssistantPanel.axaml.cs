using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Code-behind for the AI Design Assistant panel.
/// </summary>
public partial class AiAssistantPanel : UserControl
{
    public AiAssistantPanel()
    {
        InitializeComponent();

        // Handle Enter key to send message
        UserInputTextBox.KeyDown += OnUserInputKeyDown;
    }

    private void OnUserInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;

            if (DataContext is MainViewModel mainVm)
            {
                var aiVm = mainVm.RightPanel.AiAssistant;
                if (aiVm.SendMessageCommand.CanExecute(null))
                {
                    aiVm.SendMessageCommand.Execute(null);
                }
            }
        }
    }
}
