using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Creation;
using System.Linq;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Component library panel showing saved groups, PDK macros, and available component templates.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class ComponentLibraryPanel : UserControl
{
    /// <summary>Initializes the ComponentLibraryPanel and wires up VM callbacks.</summary>
    public ComponentLibraryPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var window = TopLevel.GetTopLevel(this) as Window;

        vm.LeftPanel.ComponentLibrary.ShowRenameDialogAsync = async (currentName) =>
        {
            if (window == null) return null;
            var dialog = new Views.RenameDialog(currentName);
            return await dialog.ShowDialog<string?>(window);
        };

        vm.LeftPanel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(vm.LeftPanel.SelectedGroupTemplate))
                UpdateGroupTemplateListBoxSelections(vm, vm.LeftPanel.SelectedGroupTemplate);
        };
    }

    /// <summary>Handles pointer entering a group template item (shows delete button).</summary>
    private void OnGroupItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupTemplateItemViewModel itemVm)
            itemVm.IsHovered = true;
    }

    /// <summary>Handles pointer leaving a group template item (hides delete button).</summary>
    private void OnGroupItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupTemplateItemViewModel itemVm)
            itemVm.IsHovered = false;
    }

    /// <summary>Handles selection change in the UserGroups ListBox.</summary>
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

    /// <summary>Handles selection change in the PdkGroups ListBox.</summary>
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

    private void UpdateGroupTemplateListBoxSelections(MainViewModel vm, GroupTemplate? template)
    {
        if (template == null)
        {
            ClearUserGroupsSelection();
            ClearPdkGroupsSelection();
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
