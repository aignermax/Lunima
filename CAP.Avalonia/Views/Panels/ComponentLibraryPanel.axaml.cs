using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Creation;
using System.Linq;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Component library panel showing saved groups, PDK macros, and the full component list.
/// Handles group template selection, hover state, and rename dialog wiring.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class ComponentLibraryPanel : UserControl
{
    /// <summary>Initializes the ComponentLibraryPanel and wires up view-model callbacks.</summary>
    public ComponentLibraryPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Wire rename dialog — requires a Window reference obtained via TopLevel
        vm.LeftPanel.ComponentLibrary.ShowRenameDialogAsync = async (currentName) =>
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window == null) return null;
            var dialog = new RenameDialog(currentName);
            return await dialog.ShowDialog<string?>(window);
        };

        // Sync ListBox selections when SelectedGroupTemplate changes externally
        vm.LeftPanel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(vm.LeftPanel.SelectedGroupTemplate))
                UpdateGroupTemplateListBoxSelections(vm.LeftPanel.SelectedGroupTemplate);
        };
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
        if (DataContext is not MainViewModel vm) return;
        if (sender is not ListBox listBox) return;

        if (listBox.SelectedItem is GroupTemplateItemViewModel itemVm)
        {
            vm.LeftPanel.SelectedGroupTemplate = itemVm.Template;
            ClearPdkGroupsSelection();
        }
        else if (listBox.SelectedItem == null && e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
        {
            vm.LeftPanel.SelectedGroupTemplate = null;
        }
    }

    private void OnPdkGroupsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not ListBox listBox) return;

        if (listBox.SelectedItem is GroupTemplateItemViewModel itemVm)
        {
            vm.LeftPanel.SelectedGroupTemplate = itemVm.Template;
            ClearUserGroupsSelection();
        }
        else if (listBox.SelectedItem == null && e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
        {
            vm.LeftPanel.SelectedGroupTemplate = null;
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

    /// <summary>Clears both user and PDK group selections. Called from MainWindow when needed.</summary>
    public void ClearAllGroupSelections()
    {
        ClearUserGroupsSelection();
        ClearPdkGroupsSelection();
    }

    private void UpdateGroupTemplateListBoxSelections(GroupTemplate? template)
    {
        if (DataContext is not MainViewModel vm) return;

        if (template == null)
        {
            ClearAllGroupSelections();
            return;
        }

        var userItem = vm.LeftPanel.ComponentLibrary.UserGroups.FirstOrDefault(i => i.Template == template);
        if (userItem != null)
        {
            if (UserGroupsListBox != null)
                UserGroupsListBox.SelectedItem = userItem;
            ClearPdkGroupsSelection();
            return;
        }

        var pdkItem = vm.LeftPanel.ComponentLibrary.PdkGroups.FirstOrDefault(i => i.Template == template);
        if (pdkItem != null)
        {
            if (PdkGroupsListBox != null)
                PdkGroupsListBox.SelectedItem = pdkItem;
            ClearUserGroupsSelection();
        }
    }
}
