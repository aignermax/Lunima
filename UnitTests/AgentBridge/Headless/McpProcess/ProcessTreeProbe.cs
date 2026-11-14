using System.Globalization;
using CAP_Core.Export;
using CAP_Core.Export.Processes;
using Shouldly;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>
/// A snapshot of the live (non-zombie) processes as <c>ps</c> lists them on Linux and macOS, used to
/// prove that a child left nothing running: neither a descendant seen while it ran (even one that
/// started its own process group) nor any member of its own process group (<see cref="OwnedProcess"/>
/// starts the child as a group leader, so ordinary descendants that outlive it stay in that group).
/// </summary>
internal sealed class ProcessTreeProbe
{
    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(15);
    private const int ColumnCount = 4;

    private readonly IReadOnlyList<Entry> _live;

    private ProcessTreeProbe(IReadOnlyList<Entry> live) => _live = live;

    private readonly record struct Entry(int Id, int ParentId, int GroupId);

    /// <summary>Lists every live process of the machine.</summary>
    public static async Task<ProcessTreeProbe> SnapshotAsync()
    {
        ProcessLaunchFactory.CreateDefault()
            .TryBuild("ps", ["-A", "-o", "pid=,ppid=,pgid=,stat="], null, null, out var info, out var error)
            .ShouldBeTrue(error);
        var listing = await OwnedProcessCapture.RunAsync(info, ListingTimeout);
        listing.TimedOut.ShouldBeFalse("ps did not answer");
        listing.ExitCode.ShouldBe(0, listing.Error);
        return new ProcessTreeProbe(Parse(listing.Output));
    }

    /// <summary>Live descendants of <paramref name="root"/> (children, grandchildren, …).</summary>
    public IReadOnlySet<int> DescendantsOf(int root)
    {
        var found = new HashSet<int>();
        var frontier = new Queue<int>([root]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var entry in _live)
            {
                if (entry.ParentId == parent && found.Add(entry.Id))
                    frontier.Enqueue(entry.Id);
            }
        }
        return found;
    }

    /// <summary>The processes among <paramref name="ids"/> that are still alive.</summary>
    public IReadOnlyList<int> StillAlive(IEnumerable<int> ids) =>
        ids.Intersect(_live.Select(entry => entry.Id)).ToList();

    /// <summary>Live members of process group <paramref name="group"/>.</summary>
    public IReadOnlyList<int> GroupMembers(int group) =>
        _live.Where(entry => entry.GroupId == group).Select(entry => entry.Id).ToList();

    private static List<Entry> Parse(string listing)
    {
        var live = new List<Entry>();
        foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < ColumnCount || IsZombie(columns[3]))
                continue;
            live.Add(new Entry(Number(columns[0]), Number(columns[1]), Number(columns[2])));
        }
        return live;
    }

    private static bool IsZombie(string state) => state.StartsWith('Z') || state.StartsWith('X');

    private static int Number(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
