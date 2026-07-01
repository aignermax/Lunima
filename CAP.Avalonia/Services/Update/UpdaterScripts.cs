using System.Globalization;

namespace CAP.Avalonia.Services.Update;

/// <summary>
/// Generates the platform-specific updater scripts that replace the installed app and relaunch.
/// These are pure string builders (no I/O), so the exact commands are unit-testable. The scripts
/// are run detached by <see cref="DetachedUpdaterLauncher"/> after the app quits.
/// </summary>
public static class UpdaterScripts
{
    /// <summary>Builds the macOS updater (bash): ditto-extract → de-quarantine → ad-hoc sign → near-atomic swap → relaunch.</summary>
    public static string BuildMacOs(InstallLocation target, string archivePath) =>
        MacTemplate
            .Replace("__OLD_PID__", target.ProcessId.ToString(CultureInfo.InvariantCulture))
            .Replace("__TARGET__", ShQuote(target.Root))
            .Replace("__ARCHIVE__", ShQuote(archivePath))
            .Replace("__EXE_NAME__", ShQuote(Path.GetFileName(target.ExecutablePath)));

    /// <summary>Builds the Linux updater (bash): tar-extract → directory swap → relaunch.</summary>
    public static string BuildLinux(InstallLocation target, string archivePath) =>
        LinuxTemplate
            .Replace("__OLD_PID__", target.ProcessId.ToString(CultureInfo.InvariantCulture))
            .Replace("__TARGET__", ShQuote(target.Root))
            .Replace("__ARCHIVE__", ShQuote(archivePath))
            .Replace("__EXE_NAME__", ShQuote(Path.GetFileName(target.ExecutablePath)));

    /// <summary>Builds the Windows updater (PowerShell): wait for exit → msiexec in-place upgrade → relaunch.</summary>
    public static string BuildWindows(InstallLocation target, string msiPath) =>
        WindowsTemplate
            .Replace("__OLD_PID__", target.ProcessId.ToString(CultureInfo.InvariantCulture))
            .Replace("__MSI__", PsQuote(msiPath))
            .Replace("__EXE__", PsQuote(target.ExecutablePath));

    /// <summary>Single-quotes a value for safe use in a POSIX shell (escapes embedded quotes).</summary>
    internal static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Single-quotes a value for safe use in PowerShell (doubles embedded quotes).</summary>
    internal static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private const string MacTemplate =
        """
        #!/bin/bash
        # Lunima in-place updater (macOS) — generated. Replaces the running .app and relaunches.
        set -uo pipefail
        OLD_PID=__OLD_PID__
        TARGET=__TARGET__
        ARCHIVE=__ARCHIVE__
        EXE_NAME=__EXE_NAME__
        LOG="${TMPDIR:-/tmp}/lunima-update.log"
        exec >>"$LOG" 2>&1
        echo "=== lunima update $(date) pid=$OLD_PID target=$TARGET ==="
        PARENT="$(dirname "$TARGET")"
        BACKUP="$TARGET.bak.$$"
        STAGEDIR="$PARENT/.lunima-update.$$"

        rollback() {
          echo "ROLLBACK: $1"
          [ -d "$STAGEDIR" ] && rm -rf "$STAGEDIR"
          if [ ! -e "$TARGET" ] && [ -d "$BACKUP" ]; then mv "$BACKUP" "$TARGET"; fi
          [ -d "$BACKUP" ] && [ -e "$TARGET" ] && rm -rf "$BACKUP"
          open -n "$TARGET" 2>/dev/null || true
          exit 1
        }

        # 1. wait for the running app to exit (up to ~30s)
        for _ in $(seq 1 60); do kill -0 "$OLD_PID" 2>/dev/null || break; sleep 0.5; done
        kill -0 "$OLD_PID" 2>/dev/null && rollback "app did not exit"

        # 2. extract the new bundle beside the target (same volume -> final mv is atomic)
        mkdir -p "$STAGEDIR" || rollback "mkdir stage failed"
        ditto -x -k "$ARCHIVE" "$STAGEDIR" || rollback "extract failed"
        NEW="$(find "$STAGEDIR" -maxdepth 2 -name '*.app' -type d | head -1)"
        [ -n "$NEW" ] || rollback "no .app found in archive"

        # 3. de-quarantine (prevents the Gatekeeper prompt AND App Translocation)
        xattr -dr com.apple.quarantine "$NEW" 2>/dev/null || true

        # 4. ad-hoc sign on THIS machine (Apple Silicon needs >= ad-hoc); inside-out, not recursively
        find "$NEW/Contents" \( -name '*.dylib' -o -name '*.so' \) -print0 2>/dev/null \
          | xargs -0 -I{} codesign --force --sign - "{}" 2>/dev/null || true
        [ -f "$NEW/Contents/MacOS/$EXE_NAME" ] && codesign --force --sign - "$NEW/Contents/MacOS/$EXE_NAME" 2>/dev/null || true
        codesign --force --sign - "$NEW" 2>/dev/null || rollback "codesign failed"

        # 5. near-atomic swap: move old aside, new into place
        mv "$TARGET" "$BACKUP" || rollback "could not move old bundle aside"
        mv "$NEW" "$TARGET" || rollback "could not move new bundle into place"

        # 6. relaunch the new version, then clean up
        open -n "$TARGET" || rollback "relaunch failed"
        [ -d "$STAGEDIR" ] && rm -rf "$STAGEDIR"
        rm -rf "$BACKUP"
        rm -f "$ARCHIVE" 2>/dev/null || true
        echo "=== update OK ==="
        """;

    private const string LinuxTemplate =
        """
        #!/bin/bash
        # Lunima in-place updater (Linux) — generated. Replaces the install directory and relaunches.
        set -uo pipefail
        OLD_PID=__OLD_PID__
        TARGET=__TARGET__
        ARCHIVE=__ARCHIVE__
        EXE_NAME=__EXE_NAME__
        LOG="${TMPDIR:-/tmp}/lunima-update.log"
        exec >>"$LOG" 2>&1
        echo "=== lunima update $(date) pid=$OLD_PID target=$TARGET ==="
        PARENT="$(dirname "$TARGET")"
        BACKUP="$TARGET.bak.$$"
        STAGEDIR="$PARENT/.lunima-update.$$"

        rollback() {
          echo "ROLLBACK: $1"
          [ -d "$STAGEDIR" ] && rm -rf "$STAGEDIR"
          if [ ! -e "$TARGET" ] && [ -d "$BACKUP" ]; then mv "$BACKUP" "$TARGET"; fi
          [ -d "$BACKUP" ] && [ -e "$TARGET" ] && rm -rf "$BACKUP"
          [ -x "$TARGET/$EXE_NAME" ] && ( "$TARGET/$EXE_NAME" >/dev/null 2>&1 & )
          exit 1
        }

        for _ in $(seq 1 60); do kill -0 "$OLD_PID" 2>/dev/null || break; sleep 0.5; done
        kill -0 "$OLD_PID" 2>/dev/null && rollback "app did not exit"

        mkdir -p "$STAGEDIR" || rollback "mkdir stage failed"
        tar -xzf "$ARCHIVE" -C "$STAGEDIR" || rollback "extract failed"
        # the tarball may hold the files at its root or inside one folder; locate the executable
        if [ ! -f "$STAGEDIR/$EXE_NAME" ]; then
          SUB="$(find "$STAGEDIR" -maxdepth 2 -type f -name "$EXE_NAME" | head -1)"
          [ -n "$SUB" ] && STAGEDIR="$(dirname "$SUB")"
        fi
        [ -f "$STAGEDIR/$EXE_NAME" ] || rollback "executable not found in archive"
        chmod +x "$STAGEDIR/$EXE_NAME" 2>/dev/null || true

        mv "$TARGET" "$BACKUP" || rollback "could not move old install aside"
        mv "$STAGEDIR" "$TARGET" || rollback "could not move new install into place"

        ( "$TARGET/$EXE_NAME" >/dev/null 2>&1 & ) || rollback "relaunch failed"
        rm -rf "$BACKUP"
        rm -f "$ARCHIVE" 2>/dev/null || true
        echo "=== update OK ==="
        """;

    private const string WindowsTemplate =
        """
        # Lunima in-place updater (Windows) — generated. Runs the MSI upgrade and relaunches.
        $ErrorActionPreference = 'Continue'
        $targetPid = __OLD_PID__
        $msi = __MSI__
        $exe = __EXE__
        $log = Join-Path $env:TEMP 'lunima-update.log'
        "=== lunima update $(Get-Date) pid=$targetPid ===" | Out-File -FilePath $log -Append
        try { Wait-Process -Id $targetPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}
        $p = Start-Process msiexec -ArgumentList '/i', $msi, '/qb' -Wait -PassThru
        "msiexec exit $($p.ExitCode)" | Out-File -FilePath $log -Append
        Start-Process -FilePath $exe
        """;
}
