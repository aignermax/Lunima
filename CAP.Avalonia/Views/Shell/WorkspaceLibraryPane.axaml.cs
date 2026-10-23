using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using System.ComponentModel;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns the workspace library rail and saved-group selection controls.</summary>
public partial class WorkspaceLibraryPane : UserControl
{
    private LeftPanelViewModel? _observedLibrary;

    public WorkspaceLibraryPane()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ObserveLibrary((DataContext as MainViewModel)?.LeftPanel);
        DetachedFromVisualTree += (_, _) => ObserveLibrary(null);
        DataContextChanged += (_, _) =>
        {
            if (this.GetVisualRoot() != null) ObserveLibrary((DataContext as MainViewModel)?.LeftPanel);
        };
    }

    private void ObserveLibrary(LeftPanelViewModel? library)
    {
        if (ReferenceEquals(_observedLibrary, library)) return;
        if (_observedLibrary != null) _observedLibrary.PropertyChanged -= OnLibraryPropertyChanged;
        _observedLibrary = library;
        if (_observedLibrary != null)
        {
            _observedLibrary.PropertyChanged += OnLibraryPropertyChanged;
            UpdateGroupTemplateListBoxSelections(_observedLibrary.SelectedGroupTemplate);
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftPanelViewModel.SelectedGroupTemplate))
            UpdateGroupTemplateListBoxSelections(_observedLibrary?.SelectedGroupTemplate);
    }

    public event EventHandler<RoutedEventArgs>? RegistryBrowserRequested;
    private void OpenRegistryBrowser_Click(object? sender, RoutedEventArgs e) => RegistryBrowserRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? RegistrySearchRequested;
    private void OpenRegistrySearchHint_Click(object? sender, RoutedEventArgs e) => RegistrySearchRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? EditComponentRequested;
    private void TemplateEditComponent_Click(object? sender, RoutedEventArgs e) => EditComponentRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? DeleteComponentRequested;
    private void TemplateDeleteComponent_Click(object? sender, RoutedEventArgs e) => DeleteComponentRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? CreatePdkRequested;
    private void PdkCreate_Click(object? sender, RoutedEventArgs e) => CreatePdkRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? EditPdkProcessRequested;
    private void PdkEditProcess_Click(object? sender, RoutedEventArgs e) => EditPdkProcessRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? DeletePdkRequested;
    private void PdkDelete_Click(object? sender, RoutedEventArgs e) => DeletePdkRequested?.Invoke(sender, e);

    /// <summary>
    /// Handles pointer entering a group template item (shows delete button).
    /// </summary>
    private void OnGroupItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupTemplateItemViewModel itemVm)
        {
            itemVm.IsHovered = true;
        }
    }

    /// <summary>
    /// Handles pointer leaving a group template item (hides delete button).
    /// </summary>
    private void OnGroupItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupTemplateItemViewModel itemVm)
        {
            itemVm.IsHovered = false;
        }
    }

    /// <summary>
    /// Handles selection change in UserGroups ListBox.
    /// Extracts the GroupTemplate from GroupTemplateItemViewModel and sets it in LeftPanel.
    /// </summary>
    private void OnUserGroupsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not ListBox listBox) return;

        if (listBox.SelectedItem is GroupTemplateItemViewModel itemVm)
        {
            vm.LeftPanel.SelectedGroupTemplate = itemVm.Template;
            // Clear PDK groups selection
            ClearPdkGroupsSelection();
        }
        else if (listBox.SelectedItem == null)
        {
            // Only clear if this was triggered by user action, not by code
            if (e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
            {
                vm.LeftPanel.SelectedGroupTemplate = null;
            }
        }
    }

    /// <summary>
    /// Handles selection change in PdkGroups ListBox.
    /// Extracts the GroupTemplate from GroupTemplateItemViewModel and sets it in LeftPanel.
    /// </summary>
    private void OnPdkGroupsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not ListBox listBox) return;

        if (listBox.SelectedItem is GroupTemplateItemViewModel itemVm)
        {
            vm.LeftPanel.SelectedGroupTemplate = itemVm.Template;
            // Clear user groups selection
            ClearUserGroupsSelection();
        }
        else if (listBox.SelectedItem == null)
        {
            // Only clear if this was triggered by user action, not by code
            if (e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
            {
                vm.LeftPanel.SelectedGroupTemplate = null;
            }
        }
    }

    private void ClearUserGroupsSelection()
    {
        if (UserGroupsListBox != null)
        {
            UserGroupsListBox.SelectedItem = null;
        }
    }

    private void ClearPdkGroupsSelection()
    {
        if (PdkGroupsListBox != null)
        {
            PdkGroupsListBox.SelectedItem = null;
        }
    }

    /// <summary>
    /// Clears both user and PDK group selections. Called from MainViewModel.
    /// </summary>
    public void ClearAllGroupSelections()
    {
        ClearUserGroupsSelection();
        ClearPdkGroupsSelection();
    }

    /// <summary>
    /// Updates ListBox selections to match the given GroupTemplate.
    /// Finds the corresponding GroupTemplateItemViewModel and selects it.
    /// </summary>
    public void UpdateGroupTemplateListBoxSelections(CAP_Core.Components.Creation.GroupTemplate? template)
    {
        if (DataContext is not MainViewModel vm) return;

        if (template == null)
        {
            // Clear all selections
            ClearAllGroupSelections();
        }
        else
        {
            // Find and select the matching item in UserGroups
            var userItem = vm.LeftPanel.ComponentLibrary.UserGroups.FirstOrDefault(vm => vm.Template == template);
            if (userItem != null)
            {
                if (UserGroupsListBox != null)
                {
                    UserGroupsListBox.SelectedItem = userItem;
                }
                ClearPdkGroupsSelection();
                return;
            }

            // Find and select the matching item in PdkGroups
            var pdkItem = vm.LeftPanel.ComponentLibrary.PdkGroups.FirstOrDefault(vm => vm.Template == template);
            if (pdkItem != null)
            {
                if (PdkGroupsListBox != null)
                {
                    PdkGroupsListBox.SelectedItem = pdkItem;
                }
                ClearUserGroupsSelection();
            }
        }
    }
}
