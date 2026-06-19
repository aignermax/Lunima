using System.Collections.Concurrent;
using System.Diagnostics;
using CAP_Core.Solvers.Fdtd;

namespace CAP.Avalonia.Services.Solvers;

/// <summary>
/// Implements <see cref="IFdtdSMatrixService"/> by running the open-source Meep
/// FDTD solver inside a pinned Docker image (Meep is conda-only and has no native
/// Windows build). The image is built on first use if absent. The component GDS
/// directory is volume-mounted; the request is passed as a spec file (robust under
/// MPI, unlike stdin). Raw stderr is surfaced on failure — no silent fallback.
/// </summary>
public class DockerFdtdSMatrixService : IFdtdSMatrixService
{
    /// <summary>Default per-solve timeout. 3D runs can take many minutes.</summary>
    public static readonly TimeSpan DefaultSolveTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Timeout for the one-time image build (conda solve + downloads).</summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(40);

    private const string ContainerDataDir = "/data";
    private const string ContainerScript = "/work/fdtd_sparams.py";

    /// <summary>Label stamped on every solver container so orphans can be reaped.</summary>
    private const string ContainerLabel = "lunima.fdtd=1";

    /// <summary>Names of containers currently running, killed on process exit.</summary>
    private static readonly ConcurrentDictionary<string, byte> ActiveContainers = new();
    private static int _exitHookInstalled;
    private static int _orphansReaped;

    /// <summary>
    /// Serialises solves process-wide. FDTD already saturates every core via mpirun,
    /// so overlapping runs only oversubscribe the CPU (and double the /dev/shm + RAM
    /// pressure) — they don't finish sooner. One at a time, each gets the full machine.
    /// </summary>
    internal static readonly SemaphoreSlim SolveGate = new(1, 1);

    /// <summary>Shared-memory budget per MPI rank in MB (UCX inter-rank buffers).</summary>
    private const int ShmMbPerRank = 256;

    /// <summary>Floor for /dev/shm so even a 1-2 core machine has headroom.</summary>
    private const int ShmFloorMb = 2048;

    /// <summary>Ceiling so we never request an absurd tmpfs cap on big machines.</summary>
    private const int ShmCeilingMb = 16384;

    private readonly string _dockerExe;
    private readonly string _imageTag;
    private readonly string _dockerfilePath;
    private readonly string _buildContext;
    private readonly TimeSpan _timeout;

    /// <summary>Initializes the service.</summary>
    /// <param name="imageTag">Pinned image tag, e.g. "lunima-meep:1".</param>
    /// <param name="dockerfilePath">Path to the Dockerfile used to build the image.</param>
    /// <param name="buildContext">Docker build context (repo root, so the script can be COPYed).</param>
    /// <param name="dockerExecutable">Docker CLI (default "docker").</param>
    /// <param name="timeout">Optional per-solve timeout.</param>
    public DockerFdtdSMatrixService(
        string imageTag, string dockerfilePath, string buildContext,
        string dockerExecutable = "docker", TimeSpan? timeout = null)
    {
        _imageTag = imageTag ?? throw new ArgumentNullException(nameof(imageTag));
        _dockerfilePath = dockerfilePath ?? throw new ArgumentNullException(nameof(dockerfilePath));
        _buildContext = buildContext ?? throw new ArgumentNullException(nameof(buildContext));
        _dockerExe = dockerExecutable;
        _timeout = timeout ?? DefaultSolveTimeout;
    }

    /// <inheritdoc/>
    public async Task<FdtdSMatrixResult> SolveAsync(
        FdtdSMatrixRequest request, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var hasGds = !string.IsNullOrWhiteSpace(request.GdsPath) && File.Exists(request.GdsPath);
        if (!hasGds && request.Polygons.Count == 0)
            return FdtdSMatrixResult.Fail("No geometry supplied: provide either a GDS file or polygons.");

        // Only one solve runs at a time (see SolveGate): a second is queued rather
        // than left to fight the first for the CPU. WaitAsync throws if the caller
        // cancels while queued — handled upstream as a normal cancel, not an error.
        if (SolveGate.CurrentCount == 0)
            progress?.Report("Waiting for another FDTD run to finish…");
        await SolveGate.WaitAsync(ct);
        try
        {
            return await SolveOnceAsync(request, hasGds, progress, ct);
        }
        finally
        {
            SolveGate.Release();
        }
    }

    private async Task<FdtdSMatrixResult> SolveOnceAsync(
        FdtdSMatrixRequest request, bool hasGds, IProgress<string>? progress, CancellationToken ct)
    {
        InstallExitHook();
        // Reap leftover containers from a previous (hard-killed) session ONCE per
        // process. Doing it before every solve would `docker rm -f` the containers
        // of concurrently-running solves in THIS session (they share the label) and
        // kill them mid-run ("No JSON found in FDTD solver output").
        if (Interlocked.Exchange(ref _orphansReaped, 1) == 0)
            await ReapOrphanContainersAsync();

        var provision = await EnsureImageAsync(ct);
        if (provision != null)
            return provision;

        // Working dir is mounted at /data. With a GDS we mount its folder; with
        // polygons there is no file, so we use a throwaway temp folder for the spec.
        var (workingDir, isTempDir) = hasGds
            ? (Path.GetDirectoryName(Path.GetFullPath(request.GdsPath))!, false)
            : (CreateTempDir(), true);
        var specPath = Path.Combine(workingDir, "_fdtd_request.json");
        var containerGds = hasGds ? $"{ContainerDataDir}/{Path.GetFileName(request.GdsPath)}" : string.Empty;
        var containerName = "lunima-fdtd-" + Guid.NewGuid().ToString("N");

        try
        {
            await File.WriteAllTextAsync(specPath, FdtdJsonContract.SerialiseRequest(request, containerGds), ct);
            ActiveContainers[containerName] = 0;

            var cores = ResolveCores(request);
            var si = new ProcessStartInfo { FileName = _dockerExe };
            si.ArgumentList.Add("run");
            si.ArgumentList.Add("--rm");
            // Named + labelled so we can stop it on cancel/exit and reap orphans
            // (a bare `docker run` leaves the container running in the daemon when
            // the client process — or the whole app — is killed).
            si.ArgumentList.Add("--name");
            si.ArgumentList.Add(containerName);
            si.ArgumentList.Add("--label");
            si.ArgumentList.Add(ContainerLabel);
            // MPICH/UCX uses shared memory (/dev/shm) for inter-rank transport.
            // Docker's default /dev/shm is only 64 MB, which makes MPI_Init fail
            // ("Not enough memory ... /dev/shm") once several ranks start. Scale it
            // with the rank count (which itself scales with the machine's cores).
            // /dev/shm is a tmpfs cap, not a reservation, so a generous size is free.
            si.ArgumentList.Add($"--shm-size={ResolveShmMb(cores)}m");
            si.ArgumentList.Add("-v");
            si.ArgumentList.Add($"{ToDockerPath(workingDir)}:{ContainerDataDir}");
            si.ArgumentList.Add(_imageTag);
            si.ArgumentList.Add("mpirun");
            si.ArgumentList.Add("-np");
            si.ArgumentList.Add(cores.ToString());
            si.ArgumentList.Add("python");
            si.ArgumentList.Add(ContainerScript);
            si.ArgumentList.Add($"--spec={ContainerDataDir}/_fdtd_request.json");

            var run = await SubprocessJsonRunner.RunAsync(si, string.Empty, _timeout, ct,
                onStderrLine: line => { if (IsProgressLine(line)) progress?.Report(CleanProgressLine(line)); });
            return MapRun(run);
        }
        finally
        {
            // Ensure the container is gone even on cancel/timeout (killing the docker
            // client does not stop the daemon-managed container; --rm only fires on
            // a clean exit). rm -f is a no-op if it already exited.
            await ForceRemoveContainerAsync(containerName);
            ActiveContainers.TryRemove(containerName, out _);
            try { if (File.Exists(specPath)) File.Delete(specPath); } catch { /* best-effort */ }
            if (isTempDir)
                try { Directory.Delete(workingDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private async Task ForceRemoveContainerAsync(string name)
    {
        var si = new ProcessStartInfo { FileName = _dockerExe };
        si.ArgumentList.Add("rm");
        si.ArgumentList.Add("-f");
        si.ArgumentList.Add(name);
        try { await SubprocessJsonRunner.RunAsync(si, string.Empty, TimeSpan.FromSeconds(20), CancellationToken.None); }
        catch { /* best-effort */ }
    }

    /// <summary>Removes any solver containers left over from a previous (crashed) session.</summary>
    private async Task ReapOrphanContainersAsync()
    {
        var list = new ProcessStartInfo { FileName = _dockerExe };
        list.ArgumentList.Add("ps");
        list.ArgumentList.Add("-aq");
        list.ArgumentList.Add("--filter");
        list.ArgumentList.Add($"label={ContainerLabel}");
        SubprocessJsonRunner.RunResult res;
        try { res = await SubprocessJsonRunner.RunAsync(list, string.Empty, TimeSpan.FromSeconds(20), CancellationToken.None); }
        catch { return; }

        if (res.Outcome != SubprocessJsonRunner.Outcome.Completed) return;
        foreach (var id in res.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await ForceRemoveContainerAsync(id);
    }

    private void InstallExitHook()
    {
        if (Interlocked.Exchange(ref _exitHookInstalled, 1) == 1) return;
        var dockerExe = _dockerExe;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var name in ActiveContainers.Keys)
            {
                try
                {
                    var si = new ProcessStartInfo { FileName = dockerExe, UseShellExecute = false, CreateNoWindow = true };
                    si.ArgumentList.Add("rm");
                    si.ArgumentList.Add("-f");
                    si.ArgumentList.Add(name);
                    Process.Start(si)?.WaitForExit(5000);
                }
                catch { /* best-effort on shutdown */ }
            }
        };
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lunima-fdtd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FdtdSMatrixResult MapRun(SubprocessJsonRunner.RunResult run) => run.Outcome switch
    {
        SubprocessJsonRunner.Outcome.StartFailed =>
            FdtdSMatrixResult.Fail($"Could not start Docker: {run.StartError}", missingDependency: "docker"),
        SubprocessJsonRunner.Outcome.Cancelled => FdtdSMatrixResult.Fail("FDTD solve was cancelled."),
        SubprocessJsonRunner.Outcome.TimedOut => FdtdSMatrixResult.Fail("FDTD solver timed out."),
        _ => FdtdJsonContract.ParseOutput(run.Stdout, run.Stderr),
    };

    /// <summary>
    /// Ensures the solver image exists, building it from the Dockerfile if not.
    /// Returns null on success, or a failure result describing what went wrong.
    /// </summary>
    public async Task<FdtdSMatrixResult?> EnsureImageAsync(CancellationToken ct = default)
    {
        var inspect = new ProcessStartInfo { FileName = _dockerExe };
        inspect.ArgumentList.Add("image");
        inspect.ArgumentList.Add("inspect");
        inspect.ArgumentList.Add(_imageTag);
        var probe = await SubprocessJsonRunner.RunAsync(inspect, string.Empty, TimeSpan.FromSeconds(30), ct);

        if (probe.Outcome == SubprocessJsonRunner.Outcome.StartFailed)
            return FdtdSMatrixResult.Fail($"Could not start Docker: {probe.StartError}", missingDependency: "docker");
        if (probe.Outcome == SubprocessJsonRunner.Outcome.Completed && probe.ExitCode == 0)
            return null; // image present

        var build = new ProcessStartInfo { FileName = _dockerExe };
        build.ArgumentList.Add("build");
        build.ArgumentList.Add("-f");
        build.ArgumentList.Add(_dockerfilePath);
        build.ArgumentList.Add("-t");
        build.ArgumentList.Add(_imageTag);
        build.ArgumentList.Add(_buildContext);
        var built = await SubprocessJsonRunner.RunAsync(build, string.Empty, BuildTimeout, ct);

        if (built.Outcome == SubprocessJsonRunner.Outcome.Completed && built.ExitCode == 0)
            return null;
        return FdtdSMatrixResult.Fail(
            $"Failed to provision FDTD image '{_imageTag}'. {built.Outcome}.", rawStderr: built.Stderr);
    }

    /// <summary>
    /// Picks an MPI rank count. 3D is capped lower because each rank holds a slice
    /// of the 3D grid plus DFT monitors — too many ranks exhausted RAM and the OS
    /// killed the run (the original 24-rank OOM). Honours an explicit request value.
    /// </summary>
    private static int ResolveCores(FdtdSMatrixRequest request)
    {
        if (request.Cores > 0)
            return request.Cores;
        var available = Math.Max(1, Environment.ProcessorCount);
        return request.Is3D ? Math.Min(available, 8) : Math.Min(available, 16);
    }

    /// <summary>
    /// Sizes the container's /dev/shm to the rank count (UCX shared-memory transport
    /// grows with ranks), clamped to a sane floor/ceiling. Since /dev/shm is a tmpfs
    /// cap rather than an upfront reservation, over-provisioning is harmless.
    /// </summary>
    internal static int ResolveShmMb(int cores) =>
        Math.Clamp(cores * ShmMbPerRank, ShmFloorMb, ShmCeilingMb);

    /// <summary>
    /// Keeps the noise down: only forward solver lines that carry useful progress
    /// (a tqdm/meep percentage or time-step), not every warning.
    /// </summary>
    private static bool IsProgressLine(string line) =>
        line.Contains('%') || line.Contains("time step", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Meep progress", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strips the tqdm bar art (Unicode block-element glyphs and surrounding pipes)
    /// and the log/warning tail that Meep concatenates onto the same line (the bar is
    /// written with '\r', so a following warning lands on the same "line"). Returns
    /// just the useful "50% 3/4 [00:03&lt;00:01, 1.2it/s]" part.
    /// </summary>
    internal static string CleanProgressLine(string line)
    {
        var noBlocks = System.Text.RegularExpressions.Regex.Replace(line, @"[▀-▟|]+", " ");
        // The tqdm progress ends at the first ']'; anything after it (e.g. a
        // "/opt/conda/.../meep/__init__.py:…: ComplexWarning") is concatenated noise.
        var bracket = noBlocks.IndexOf(']');
        if (bracket >= 0)
            noBlocks = noBlocks[..(bracket + 1)];
        return System.Text.RegularExpressions.Regex.Replace(noBlocks, @"\s{2,}", " ").Trim();
    }

    private static string ToDockerPath(string path) => path.Replace('\\', '/');
}
