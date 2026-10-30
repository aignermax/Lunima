using CAP_Core.Preferences;
namespace CAP.Avalonia.Services;

/// <summary>
/// User preferences data structure.
/// </summary>
public class UserPreferences
{
    /// <summary>Offer visual first-use guidance until it is completed or explicitly dismissed.</summary>
    public bool GuidedLearningEnabled { get; set; } = true;
    public List<int> CompletedLearningChapters { get; set; } = new();
    public int LastLearningChapter { get; set; } = 1;

    /// <summary>
    /// List of PDK names that are currently enabled (visible in library).
    /// </summary>
    public List<string> EnabledPdks { get; set; } = new();

    /// <summary>
    /// All PDK names loaded at the last filter-state save (enabled or not) — lets restore tell a
    /// deliberately-unchecked PDK from one it has never seen. See
    /// <see cref="UserPreferencesService.GetKnownPdks"/>.
    /// </summary>
    public List<string> KnownPdks { get; set; } = new();

    /// <summary>
    /// List of user-loaded PDK file paths to auto-load at startup.
    /// </summary>
    public List<string> UserPdkPaths { get; set; } = new();

    /// <summary>
    /// Width of the left panel in pixels (default 220).
    /// </summary>
    public double LeftPanelWidth { get; set; } = 220;

    /// <summary>
    /// Width of the right panel in pixels (default 250).
    /// </summary>
    public double RightPanelWidth { get; set; } = 250;

    /// <summary>
    /// Custom Python executable path for Nazca/GDS export.
    /// If null, system default (python3/python) will be used.
    /// </summary>
    public string? CustomPythonPath { get; set; }

    /// <summary>
    /// Version string the user chose to skip during update prompts (e.g. "1.2.3").
    /// Null means no version is skipped.
    /// </summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>
    /// UTC date when the user last chose "Skip for Today".
    /// Null means never skipped. Reset daily.
    /// </summary>
    public DateTime? SkipTodayDate { get; set; }

    /// <summary>
    /// Encrypted API key for the AI Design Assistant (Claude/Anthropic).
    /// Empty string means no key is configured.
    /// </summary>
    public string AiApiKey { get; set; } = "";

    /// <summary>
    /// API key for the Tidy3D cloud solver (FDTD S-matrix backend).
    /// Empty string means no key is configured.
    /// </summary>
    public string Tidy3dApiKey { get; set; } = "";

    /// <summary>
    /// Persisted FDTD S-matrix backend choice as an <c>FdtdBackendType</c> name
    /// (e.g. "MeepDocker" or "Tidy3D"). Null/unknown falls back to Meep/Docker.
    /// </summary>
    public string? FdtdBackend { get; set; }

    /// <summary>
    /// Default chip width in millimeters for new projects (default 5 mm).
    /// </summary>
    public double DefaultChipWidthMm { get; set; } = 5.0;

    /// <summary>
    /// Default chip height in millimeters for new projects (default 5 mm).
    /// </summary>
    public double DefaultChipHeightMm { get; set; } = 5.0;

    /// <summary>
    /// Recently opened project files, most recently opened first.
    /// Maintained by <see cref="RecentProjectsService"/>.
    /// </summary>
    public List<RecentProjectEntry> RecentProjects { get; set; } = new();

    /// <summary>
    /// When true, the app reopens the most recent project at startup
    /// instead of showing the Home screen (default false).
    /// </summary>
    public bool ReopenLastProjectOnStartup { get; set; }

    /// <summary>
    /// Global interconnect waveguide width in µm. Null = export default (0.45).
    /// </summary>
    public double? InterconnectWidthMicrometers { get; set; }

    /// <summary>
    /// Global interconnect bend radius in µm. Null = export default (50).
    /// </summary>
    public double? InterconnectBendRadiusMicrometers { get; set; }

    /// <summary>
    /// Global interconnect GDS layer. Null = PDK/Nazca default layer.
    /// </summary>
    public int? InterconnectGdsLayer { get; set; }

    /// <summary>
    /// UI language: a shipped language code ("en", "de", "zh-Hans", "es") or
    /// "system" (default) to auto-detect the OS display language at startup.
    /// </summary>
    public string UiLanguage { get; set; } = "system";

    /// <summary>
    /// Whether adaptive crossing insertion is enabled for routing.
    /// On for new/unset preferences; an explicitly saved false remains off.
    /// </summary>
    public bool CrossingInsertionEnabled { get; set; } = true;

    /// <summary>
    /// Whether diagonal (45°) waveguide routing is enabled. Off by default —
    /// classic Manhattan-only routing is the baseline.
    /// </summary>
    public bool UseDiagonalRouting { get; set; }

    /// <summary>
    /// Whether new/re-routed connections try the direct styled geometry (straight /
    /// S-bend / sine / cobra) first, using A* only as the obstacle-avoidance fallback.
    /// On by default — the smooth direct route is what a photonics designer expects.
    /// </summary>
    public bool PreferDirectStyledRoutes { get; set; } = true;

    /// <summary>
    /// Whether the local MCP agent bridge server runs. Off by default — it lets a local
    /// AI agent inspect and edit the open design, so the user has to opt in.
    /// </summary>
    public bool AgentBridgeEnabled { get; set; }

    /// <summary>Loopback TCP port of the agent bridge. 0 means "use the default port".</summary>
    public int AgentBridgePort { get; set; }

    /// <summary>
    /// Per-install bearer token the agent bridge requires on every request. Empty until
    /// first read, when a random token is generated and persisted.
    /// </summary>
    public string AgentBridgeToken { get; set; } = "";

    /// <summary>
    /// Whether the agent bridge requires <see cref="AgentBridgeToken"/> as a bearer token.
    /// Off by default: the server only binds 127.0.0.1 and rejects non-loopback Host/Origin
    /// headers, and a same-user process could read the token from this file anyway.
    /// </summary>
    public bool AgentBridgeRequireToken { get; set; }

    /// <summary>
    /// App version for which the user answered "Not now" to the "Connect Claude Code"
    /// prompt; the prompt stays hidden until the next version. Null when never dismissed.
    /// </summary>
    public string? ClaudeCodePromptDismissedVersion { get; set; }
}
