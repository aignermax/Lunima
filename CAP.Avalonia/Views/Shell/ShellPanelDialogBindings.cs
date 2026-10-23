using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.Services.Notifications;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.PdkImport;
using CAP.Avalonia.ViewModels.Properties.Editors;
using CAP.Avalonia.Views.PdkImport;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns optional dialog callbacks and notifications attached to one shell window.</summary>
internal sealed class ShellPanelDialogBindings(PdkImportService? imports = null,
    OnaAnalyzerEditorProvider? ona = null, NotificationService? notifications = null) : IDisposable
{
    private readonly CallbackBindings _bindings = new();
    private IDisposable? _notificationAttachment;

    public void Attach(Window owner, LeftPanelViewModel library, Func<Component, Task> openAnalyzer)
    {
        if (ona != null) _bindings.Bind(() => ona.OpenSweepAsync, value => ona.OpenSweepAsync = value, openAnalyzer);
        if (imports != null)
            _bindings.Bind(() => library.ShowImportWizardAsync, value => library.ShowImportWizardAsync = value,
                async path => await new PdkImportWizardWindow
                { DataContext = new PdkImportWizardViewModel(path, imports) }.ShowDialog<string?>(owner));
        if (notifications != null)
        {
            var manager = new WindowNotificationManager(owner) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
            _notificationAttachment = notifications.AttachOwned(manager);
        }
    }

    public void Dispose() { _bindings.Dispose(); _notificationAttachment?.Dispose(); }
}
