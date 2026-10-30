using CAP_Core.Export.Processes;
using System.Text.Json;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class OwnedToolProcessPublicationTests
{
    [SkippableTheory]
    [InlineData("descendant.pid")]
    [InlineData("closed.txt")]
    public async Task ProducerSignal_IsInvisibleUntilTheProducerClosesItsWriter(string markerName)
    {
        var holdPidWriter = $$"""
            import builtins
            original_open = builtins.open
            directory = os.path.dirname(os.path.abspath(__file__))
            release_path = os.path.join(directory, "release-writer")
            held_marker = {{JsonSerializer.Serialize(markerName)}}
            class HeldPidWriter:
                def __init__(self, handle):
                    self.handle = handle
                def __enter__(self):
                    return self.handle.__enter__()
                def __exit__(self, *args):
                    with original_open(os.path.join(directory, "writer-held"), "w") as marker:
                        marker.write("ready")
                    while not os.path.exists(release_path):
                        time.sleep(0.01)
                    return self.handle.__exit__(*args)
            def held_open(path, *args, **kwargs):
                handle = original_open(path, *args, **kwargs)
                if os.path.basename(path) in (held_marker, held_marker + ".pending"):
                    return HeldPidWriter(handle)
                return handle
            builtins.open = held_open
            """;
        var producer = markerName == "descendant.pid"
            ? OwnedToolProcessScenarios.SpawnDescendant(inheritPipes: true)
            : "publish_fixture_signal(os.path.join(directory, 'closed.txt'), 'closed')";
        await using var child = await ControlledSolverProcess.CreateAsync(holdPidWriter + "\n"
            + producer + "\nos._exit(0)");
        var directory = Path.GetDirectoryName(child.ScriptPath)!;
        var signalPath = Path.Combine(directory, markerName);
        using var stop = new CancellationTokenSource();
        var running = OwnedProcessCapture.RunAsync(child.StartInfo, TimeSpan.FromSeconds(30), stop.Token);
        try
        {
            using var guard = new CancellationTokenSource(ControlledSolverProcess.GuardTimeout);
            while (!File.Exists(Path.Combine(directory, "writer-held")))
                await Task.Delay(10, guard.Token);

            File.Exists(signalPath).ShouldBeFalse(
                "the readiness path must not expose an open writer or a partially written signal");
            Task consumer = markerName == "descendant.pid"
                ? OwnedToolProcessScenarios.AwaitDescendantAsync(child)
                : ScriptProcessLifetimeFixture.ReadMarkerAsync(child, markerName);
            consumer.IsCompleted.ShouldBeFalse();
            await File.WriteAllTextAsync(Path.Combine(directory, "release-writer"), "release");
            await consumer.WaitAsync(ControlledSolverProcess.GuardTimeout);
            var signal = await File.ReadAllTextAsync(signalPath);
            if (markerName == "descendant.pid")
            {
                int.TryParse(signal, out var pid).ShouldBeTrue();
                pid.ShouldBeGreaterThan(0);
            }
            else signal.ShouldBe("closed");
            (await running.WaitAsync(ControlledSolverProcess.GuardTimeout)).ExitCode.ShouldBe(0);
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally
        {
            // The native owner, not the intentionally unsafe baseline PID file, performs
            // rescue cleanup while the producer may still have that file open.
            await File.WriteAllTextAsync(Path.Combine(directory, "release-writer"), "release");
            stop.Cancel();
            try { await running.WaitAsync(ControlledSolverProcess.GuardTimeout); }
            catch (OperationCanceledException) when (running.IsCompleted) { }
        }
    }
}
