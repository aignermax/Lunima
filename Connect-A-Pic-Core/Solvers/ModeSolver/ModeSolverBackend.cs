namespace CAP_Core.Solvers.ModeSolver;

/// <summary>
/// Selects the Python mode-solver backend to use when calling <c>mode_solve.py</c>.
/// </summary>
public enum ModeSolverBackend
{
    /// <summary>
    /// gdsfactory.simulation.modes / femwell — Apache 2.0, broad community,
    /// recommended Phase-1 default.
    /// Install: <c>pip install gdsfactory[femwell]</c>
    /// </summary>
    GdsfactoryModes,

    /// <summary>
    /// EMpy — lightweight pure-Python FDE solver for 2D cross-sections.
    /// Install: <c>pip install EMpy</c>
    /// </summary>
    EMpy,

    /// <summary>
    /// Tidy3D cloud solver — highest accuracy, requires credits/API key.
    /// Install: <c>pip install tidy3d</c>
    /// </summary>
    Tidy3D,
}
