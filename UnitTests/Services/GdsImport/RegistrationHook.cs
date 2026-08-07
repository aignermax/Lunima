using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Deterministic mid-run hook for GDS import cancellation tests: runs
/// <see cref="OnFirstRegistration"/> synchronously inside the run's FIRST
/// component registration — after the off-thread parse finished, before any
/// token re-read or canvas placement — so a cancel or dialog close lands at
/// exactly that moment. An ungated cancel from the test thread races the
/// thread pool and can land after the whole run already completed.
/// </summary>
internal sealed class RegistrationHook
{
    private int _armed = 1;

    /// <summary>Invoked once, on the run's own thread, at the first registration.</summary>
    public Action? OnFirstRegistration { get; set; }

    /// <summary>True once the hook fired (guards against a run that never registers).</summary>
    public bool Fired => Volatile.Read(ref _armed) == 0;

    /// <summary>Wraps a register callback so its first invocation runs the hook.</summary>
    public Action<PdkComponentDraft, string, string> Wrap(
        Action<PdkComponentDraft, string, string> register) =>
        (draft, pdkName, filePath) =>
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
                OnFirstRegistration?.Invoke();
            register(draft, pdkName, filePath);
        };
}
