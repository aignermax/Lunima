namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// Chooses where each wire of a pin cluster leaves the face: straight out (0), or turning
/// towards the lower (−1) or higher (+1) lateral side. A wire whose far end lies ahead of the
/// pin goes straight; otherwise it turns towards the side its far end is on. Wires of one
/// face can only fan out without crossing when the sides are ordered like the pins: every
/// −1 below every 0 below every +1 (pins sorted by lateral position). Disordered wishes are
/// repaired with the fewest changes — the wires that lose their wish need a detour or a
/// crossing further out, where there is room for one.
/// </summary>
public static class BreakoutSideSelector
{
    /// <summary>The side choices, in the order lower side, straight, higher side.</summary>
    private static readonly int[] Sides = { -1, 0, 1 };

    /// <summary>
    /// The preferred side of one wire, from its far end in the pin frame:
    /// <paramref name="forward"/> along the outward heading and <paramref name="lateral"/>
    /// along heading + 90°.
    /// </summary>
    public static int PreferredSide(double forward, double lateral)
    {
        if (forward >= Math.Abs(lateral)) return 0;
        return Math.Sign(lateral);
    }

    /// <summary>
    /// The closest monotone (non-decreasing) side assignment to <paramref name="wishes"/>:
    /// fewest changed entries; ties keep more wires straight, then the lower side first.
    /// </summary>
    public static int[] MakeMonotone(IReadOnlyList<int> wishes)
    {
        int n = wishes.Count;
        if (n == 0) return Array.Empty<int>();

        // cost[i, s]: fewest changes for wishes[0..i] with entry i on Sides[s].
        var cost = new int[n, Sides.Length];
        var from = new int[n, Sides.Length];
        for (int s = 0; s < Sides.Length; s++)
            cost[0, s] = Penalty(wishes[0], Sides[s]);
        for (int i = 1; i < n; i++)
        {
            for (int s = 0; s < Sides.Length; s++)
            {
                int best = 0;
                for (int prev = 1; prev <= s; prev++)
                    if (cost[i - 1, prev] < cost[i - 1, best]) best = prev;
                cost[i, s] = cost[i - 1, best] + Penalty(wishes[i], Sides[s]);
                from[i, s] = best;
            }
        }

        int end = 0;
        for (int s = 1; s < Sides.Length; s++)
            if (cost[n - 1, s] < cost[n - 1, end]) end = s;
        var result = new int[n];
        for (int i = n - 1; i >= 0; i--)
        {
            result[i] = Sides[end];
            end = from[i, end];
        }
        return result;
    }

    /// <summary>
    /// Cost of assigning <paramref name="side"/> to a wire that wishes <paramref name="wish"/>:
    /// two per changed wish, one less when the change goes to straight (a straight breakout can
    /// still turn either way right after its anchor).
    /// </summary>
    private static int Penalty(int wish, int side)
    {
        if (wish == side) return 0;
        return side == 0 ? 1 : 2;
    }
}
