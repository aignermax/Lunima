using CAP_Core.Components.Core;

namespace CAP_DataAccess.Persistence.DTOs;

/// <summary>
/// Packs a polygon's vertices into a short string: every coordinate is rounded to
/// whole nanometres (the GDS database grid), stored as the difference to the
/// previous coordinate of the same axis, zig-zag mapped to an unsigned value,
/// written as a LEB128 varint, and the bytes are Base64 encoded. Neighbouring
/// vertices of drawn geometry sit close together, so most deltas take one or two bytes.
/// </summary>
public static class PolygonPointCodec
{
    private const double NanometresPerMicrometre = 1000.0;

    /// <summary>Encodes the vertices (µm).</summary>
    /// <param name="points">Vertices in micrometres.</param>
    public static string Encode(IReadOnlyList<OutlinePoint> points)
    {
        var bytes = new List<byte>(points.Count * 4);
        long previousX = 0, previousY = 0;
        foreach (var point in points)
        {
            long x = (long)Math.Round(point.X * NanometresPerMicrometre);
            long y = (long)Math.Round(point.Y * NanometresPerMicrometre);
            WriteVarint(bytes, ZigZag(x - previousX));
            WriteVarint(bytes, ZigZag(y - previousY));
            previousX = x;
            previousY = y;
        }
        return Convert.ToBase64String(bytes.ToArray());
    }

    /// <summary>Decodes vertices (µm), or returns null for malformed input.</summary>
    /// <param name="encoded">A string produced by <see cref="Encode"/>.</param>
    public static List<OutlinePoint>? TryDecode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }

        var points = new List<OutlinePoint>();
        long x = 0, y = 0;
        int index = 0;
        while (index < bytes.Length)
        {
            if (!TryReadVarint(bytes, ref index, out var dx) || !TryReadVarint(bytes, ref index, out var dy))
                return null;
            x += UnZigZag(dx);
            y += UnZigZag(dy);
            points.Add(new OutlinePoint(x / NanometresPerMicrometre, y / NanometresPerMicrometre));
        }
        return points;
    }

    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    private static long UnZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    private static void WriteVarint(List<byte> bytes, ulong value)
    {
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
    }

    private static bool TryReadVarint(byte[] bytes, ref int index, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (index >= bytes.Length) return false;
            byte b = bytes[index++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }
}
