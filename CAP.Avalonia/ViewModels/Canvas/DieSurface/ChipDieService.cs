using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.ViewModels.Canvas.DieSurface;

/// <summary>
/// Every design is an assembly of at least one die. A design without die data — every file
/// saved before dies existed, and every new design — is shown as its one die "Die 1": the chip
/// rectangle with its size and the design's process, drawn as a die frame, selectable and
/// framed in the System workspace, with die properties. The model stays a single-die design (no
/// die group, the file and every export unchanged) until a die property is edited; that edit
/// turns the chip into a real die in one undo step (see <c>PromoteChipToDieCommand</c>).
/// <see cref="ViewModel"/> stands for the chip die wherever a die is expected (selection,
/// properties, zoom to die); it never joins the canvas collections.
/// </summary>
public sealed class ChipDieService
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _proxy;
    private readonly ComponentViewModel _viewModel;

    /// <summary>Creates the chip die over <paramref name="canvas"/>.</summary>
    public ChipDieService(DesignCanvasViewModel canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _proxy = new ComponentGroup(Name) { Identifier = Name, HumanReadableName = Name };
        Sync();
        _viewModel = new ComponentViewModel(_proxy);
    }

    /// <summary>The chip die's name — the first default die name.</summary>
    public static string Name { get; } = DieAssembly.NextDieName(Array.Empty<string>());

    /// <summary>The design's process (the chip die's process); wired by the main view model.</summary>
    public Func<ActiveProcessSelection?>? ProcessSource { get; set; }

    /// <summary>True when the canvas shows a single-die design at its root (the chip is Die 1).</summary>
    public bool IsShown => !_canvas.IsInGroupEditMode && !_canvas.Dies.IsAssemblyRoot;

    /// <summary>The chip rectangle — the outline of Die 1.</summary>
    public DieOutline Outline => new(_canvas.ChipMinX, _canvas.ChipMinY,
        Math.Max(0, _canvas.ChipMaxX - _canvas.ChipMinX), Math.Max(0, _canvas.ChipMaxY - _canvas.ChipMinY));

    /// <summary>The die standing for the chip, brought in line with the chip size and process.</summary>
    public ComponentGroup Die
    {
        get
        {
            Sync();
            return _proxy;
        }
    }

    /// <summary>The view model selected for the chip die (never part of the canvas collections).</summary>
    public ComponentViewModel ViewModel
    {
        get
        {
            Sync();
            _viewModel.X = _proxy.PhysicalX;
            _viewModel.Y = _proxy.PhysicalY;
            return _viewModel;
        }
    }

    /// <summary>True when <paramref name="component"/> is the stand-in of the chip die.</summary>
    public bool IsProxy(Component? component) => ReferenceEquals(component, _proxy);

    /// <summary>True when the chip die is the selected item.</summary>
    public bool IsSelected => ReferenceEquals(_canvas.SelectedComponent, _viewModel);

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) lies on the shown chip die.</summary>
    public bool Contains(double x, double y) => IsShown && Outline.Contains(x, y);

    private void Sync()
    {
        var outline = Outline;
        _proxy.PhysicalX = outline.X;
        _proxy.PhysicalY = outline.Y;
        if (DieDefinition.IsValidDimension(outline.Width) && DieDefinition.IsValidDimension(outline.Height)
            && (_proxy.Die is not { } current
                || current.WidthMicrometers != outline.Width || current.HeightMicrometers != outline.Height))
            _proxy.Die = new DieDefinition(outline.Width, outline.Height);
        _proxy.ProcessBinding = ProcessSource?.Invoke();
    }
}
