using System.Text;

namespace UnitTests.Helpers;

/// <summary>
/// Parsed representation of a GDS design for roundtrip verification.
/// </summary>
public class GdsDesign
{
    /// <summary>SREF elements representing component placements.</summary>
    public List<GdsSRef> ComponentRefs { get; } = new();

    /// <summary>PATH elements (centerline waveguide paths — not typically emitted by Nazca).</summary>
    public List<GdsPath> WaveguidePaths { get; } = new();

    /// <summary>
    /// Number of BOUNDARY elements. Nazca exports waveguide geometry as solid polygon
    /// boundaries rather than PATH centerlines, so this is the key geometry count.
    /// </summary>
    public int BoundaryCount { get; set; }

    /// <summary>Database-unit-per-user-unit conversion factor (typically 0.001 for nm→µm).</summary>
    public double DbUnitsPerUserUnit { get; set; } = 0.001;
}

/// <summary>
/// Structure reference (SREF) element — represents a placed component cell.
/// </summary>
public class GdsSRef
{
    /// <summary>Referenced cell name (Nazca component function name).</summary>
    public string CellName { get; set; } = "";

    /// <summary>X position in database units (divide by DbUnitsPerUserUnit to get µm).</summary>
    public int XDb { get; set; }

    /// <summary>Y position in database units.</summary>
    public int YDb { get; set; }

    /// <summary>Rotation angle in degrees (from ANGLE record).</summary>
    public double AngleDegrees { get; set; }

    /// <summary>X position in µm (Nazca coordinate system, Y-inverted vs editor).</summary>
    public double XMicron(double dbPerUser) => XDb * dbPerUser;

    /// <summary>Y position in µm (Nazca coordinate system).</summary>
    public double YMicron(double dbPerUser) => YDb * dbPerUser;
}

/// <summary>
/// PATH element — represents a waveguide segment polyline.
/// </summary>
public class GdsPath
{
    /// <summary>GDS layer number.</summary>
    public int Layer { get; set; }

    /// <summary>Width in database units.</summary>
    public int WidthDb { get; set; }

    /// <summary>XY coordinate pairs in database units.</summary>
    public List<(int X, int Y)> Points { get; } = new();

    /// <summary>Number of path segments (point pairs - 1).</summary>
    public int SegmentCount => Math.Max(0, Points.Count - 1);
}

/// <summary>
/// Minimal GDSII binary file reader for roundtrip test verification.
/// Parses SREF (component placements) and PATH (waveguide paths) elements.
/// GDSII record format: [2-byte big-endian length][1-byte type][1-byte datatype][data...]
/// </summary>
public static class GdsReader
{
    private const byte RecordBoundary = 0x08;
    private const byte RecordSRef = 0x0A;
    private const byte RecordPath = 0x09;
    private const byte RecordSName = 0x12;
    private const byte RecordXy = 0x10;
    private const byte RecordAngle = 0x1A;
    private const byte RecordEndEl = 0x11;
    private const byte RecordLayer = 0x0D;
    private const byte RecordWidth = 0x0F;
    private const byte RecordUnits = 0x03;
    private const byte RecordEndLib = 0x04;

    /// <summary>
    /// Reads a GDSII file and returns the parsed design data.
    /// Returns null if the file is not a valid GDSII file.
    /// </summary>
    public static GdsDesign? ReadFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 4)
            return null;

        var design = new GdsDesign();
        int pos = 0;

        bool inSRef = false;
        bool inPath = false;
        var currentSRef = new GdsSRef();
        var currentPath = new GdsPath();

        while (pos + 4 <= bytes.Length)
        {
            int recordLength = ReadInt16Be(bytes, pos);
            if (recordLength < 4 || pos + recordLength > bytes.Length)
                break;

            byte recordType = bytes[pos + 2];
            byte dataType = bytes[pos + 3];
            int dataLength = recordLength - 4;
            int dataOffset = pos + 4;

            switch (recordType)
            {
                case RecordUnits when dataType == 0x05 && dataLength >= 16:
                    design.DbUnitsPerUserUnit = ReadIbmDouble(bytes, dataOffset);
                    break;

                case RecordBoundary:
                    design.BoundaryCount++;
                    inSRef = false;
                    inPath = false;
                    break;

                case RecordSRef:
                    inSRef = true;
                    inPath = false;
                    currentSRef = new GdsSRef();
                    break;

                case RecordPath:
                    inPath = true;
                    inSRef = false;
                    currentPath = new GdsPath();
                    break;

                case RecordSName when inSRef && dataType == 0x06:
                    currentSRef.CellName = ReadString(bytes, dataOffset, dataLength);
                    break;

                case RecordLayer when inPath && dataType == 0x02:
                    currentPath.Layer = ReadInt16Be(bytes, dataOffset);
                    break;

                case RecordWidth when inPath && dataType == 0x03:
                    currentPath.WidthDb = ReadInt32Be(bytes, dataOffset);
                    break;

                case RecordXy when dataType == 0x03:
                    ProcessXyRecord(bytes, dataOffset, dataLength, inSRef, inPath,
                        currentSRef, currentPath);
                    break;

                case RecordAngle when inSRef && dataType == 0x05 && dataLength >= 8:
                    currentSRef.AngleDegrees = ReadIbmDouble(bytes, dataOffset);
                    break;

                case RecordEndEl:
                    if (inSRef) design.ComponentRefs.Add(currentSRef);
                    if (inPath) design.WaveguidePaths.Add(currentPath);
                    inSRef = false;
                    inPath = false;
                    break;

                case RecordEndLib:
                    return design;
            }

            pos += recordLength;
        }

        return design;
    }

    private static void ProcessXyRecord(
        byte[] bytes, int dataOffset, int dataLength,
        bool inSRef, bool inPath,
        GdsSRef currentSRef, GdsPath currentPath)
    {
        int pointCount = dataLength / 8;
        for (int i = 0; i < pointCount; i++)
        {
            int x = ReadInt32Be(bytes, dataOffset + i * 8);
            int y = ReadInt32Be(bytes, dataOffset + i * 8 + 4);

            if (inSRef && i == 0)
            {
                currentSRef.XDb = x;
                currentSRef.YDb = y;
            }
            else if (inPath)
            {
                currentPath.Points.Add((x, y));
            }
        }
    }

    /// <summary>Converts IBM 360 double (8 bytes, big-endian) to a C# double.</summary>
    internal static double ReadIbmDouble(byte[] data, int offset)
    {
        if (offset + 8 > data.Length) return 0.0;

        bool negative = (data[offset] & 0x80) != 0;
        int exponent = (data[offset] & 0x7F) - 64;

        long mantissa = 0;
        for (int i = 1; i <= 7; i++)
            mantissa = (mantissa << 8) | data[offset + i];

        if (mantissa == 0) return 0.0;

        double value = mantissa * Math.Pow(2, -56) * Math.Pow(16, exponent);
        return negative ? -value : value;
    }

    private static int ReadInt32Be(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return 0;
        return (data[offset] << 24) | (data[offset + 1] << 16)
             | (data[offset + 2] << 8) | data[offset + 3];
    }

    private static int ReadInt16Be(byte[] data, int offset)
    {
        if (offset + 2 > data.Length) return 0;
        return (short)((data[offset] << 8) | data[offset + 1]);
    }

    private static string ReadString(byte[] data, int offset, int length)
    {
        if (offset + length > data.Length) return "";
        // GDSII strings are null-padded to even length
        int nullPos = Array.IndexOf(data, (byte)0, offset, length);
        int actualLen = nullPos >= 0 ? nullPos - offset : length;
        return Encoding.ASCII.GetString(data, offset, actualLen);
    }
}
