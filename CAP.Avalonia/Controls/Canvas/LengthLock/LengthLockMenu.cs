using Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Canvas.LengthLock;

/// <summary>
/// The menu a padlock opens at the pointer: what the lock holds (or that it no longer holds),
/// re-equalize the whole lock group, or release the lock of this route or of the whole group.
/// </summary>
internal static class LengthLockMenu
{
    /// <summary>Opens the menu for <paramref name="connection"/> over <paramref name="anchor"/>.</summary>
    public static void Open(Control anchor, WaveguideConnectionViewModel connection, MainViewModel main)
    {
        var locks = main.BottomPanel.LengthEqualization.Locks;
        var group = locks.GroupOf(connection);
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        menu.Items.Add(new MenuItem { Header = locks.Describe(connection), IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Format("Routing.LengthLock.ReEqualize", group.Count), () =>
        {
            _ = locks.ReEqualizeAsync(connection);
            main.StatusText = locks.Describe(connection);
        }));
        menu.Items.Add(Item(LocalizationService.Instance.Translate("Routing.LengthLock.ReleaseOne"),
            () => Released(main, locks.Release(new[] { connection }))));
        if (group.Count > 1)
        {
            menu.Items.Add(Item(Format("Routing.LengthLock.ReleaseGroup", group.Count),
                () => Released(main, locks.Release(group))));
        }
        menu.Open(anchor);
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static void Released(MainViewModel main, int count) =>
        main.StatusText = LengthEqualizationStatus.Format("Routing.LengthEqualize.Released", count);

    private static string Format(string key, params object[] arguments) =>
        LengthEqualizationStatus.Format(key, arguments);
}
