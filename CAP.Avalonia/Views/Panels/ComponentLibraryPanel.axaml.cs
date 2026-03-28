using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Creation;
using System.Linq;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Code-behind for the ComponentLibrary panel.
/// Handles group template selection synchronization and hover state management.
/// </summary>
public partial class ComponentLibraryPanel : UserControl
{
    private LeftPanelViewModel? _leftPanel;

    /// <summary>Initializes a new instance of <see cref="ComponentLibraryPanel"/>.</summary>
    public ComponentLibraryPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LeftPanelViewModel vm)
        {
            _leftPanel = vm;
            vm.PropertyChanged += OnLeftPanelPropertyChanged;
        }
    }

    private void OnLeftPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftPanelViewModel.SelectedGroupTemplate))
        {
            UpdateGroupTemplateListBoxSelections(_leftPanel?.SelectedGroupTemplate);
        }
    }

    private void OnGroupItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupTemplateItemViewModel itemVm)
            itemVm.IsHovered = true;
    }

    private void OnGroupItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupTemplateItemViewModel itemVm)
            itemVm.IsHovered = false;
    }

    private void OnUserGroupsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_leftPanel == null || sender is not ListBox listBox) return;

        if (listBox.SelectedItem is GroupTemplateItemViewModel itemVm)
        {
            _leftPanel.SelectedGroupTemplate = itemVm.Template;
            ClearPdkGroupsSelection();
        }
        else if (listBox.SelectedItem == null && e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
        {
            _leftPanel.SelectedGroupTemplate = null;
        }
    }

    private void OnPdkGroupsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_leftPanel == null || sender is not ListBox listBox) return;

        if (listBox.SelectedItem is GroupTemplateItemViewModel itemVm)
        {
            _leftPanel.SelectedGroupTemplate = itemVm.Template;
            ClearUserGroupsSelection();
        }
        else if (listBox.SelectedItem == null && e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
        {
            _leftPanel.SelectedGroupTemplate = null;
        }
    }

    private void ClearUserGroupsSelection()
    {
        if (UserGroupsListBox != null)
            UserGroupsListBox.SelectedItem = null;
    }

    private void ClearPdkGroupsSelection()
    {
        if (PdkGroupsListBox != null)
            PdkGroupsListBox.SelectedItem = null;
    }

    private void UpdateGroupTemplateListBoxSelections(GroupTemplate? template)
    {
        if (template == null)
        {
            ClearUserGroupsSelection();
            ClearPdkGroupsSelection();
            return;
        }

        var userItem = _leftPanel?.ComponentLibrary.UserGroups.FirstOrDefault(vm => vm.Template == template);
        if (userItem != null)
        {
            if (UserGroupsListBox != null)
                UserGroupsListBox.SelectedItem = userItem;
            ClearPdkGroupsSelection();
            return;
        }

        var pdkItem = _leftPanel?.ComponentLibrary.PdkGroups.FirstOrDefault(vm => vm.Template == template);
        if (pdkItem != null)
        {
            if (PdkGroupsListBox != null)
                PdkGroupsListBox.SelectedItem = pdkItem;
            ClearUserGroupsSelection();
        }
    }
}
