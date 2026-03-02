#!/usr/bin/env dotnet-script
// SiEPIC .sparam to Connect-A-PIC PDK JSON converter
// Usage: dotnet script SparamConverter.cs
//
// Reads .sparam files from the sparam-data/ directory and produces
// a siepic-ebeam-pdk.json file for use with Connect-A-PIC Pro.
//
// The .sparam format (Lumerical Interconnect) consists of blocks:
//   Header: ('port X','MODE',mode_id,'port Y',mode_id,'transmission')
//   Shape:  (N,3)
//   Data:   frequency_Hz  magnitude  phase_radians
//
// The GC .txt format packs all S-params per row:
//   freq |S11| ang(S11) |S21| ang(S21) |S12| ang(S12) |S22| ang(S22)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

const double SpeedOfLight = 299792458.0; // m/s

// ── Data structures ──

record SparamBlock(string OutPort, string InPort, string Mode, List<(double FreqHz, double Mag, double PhaseRad)> Data);

record ComponentSpec(
    string Name, string Category, string NazcaFunction, string NazcaParameters,
    double WidthUm, double HeightUm, PinSpec[] Pins, string SourceFile, string Format);

record PinSpec(string Name, double OffsetX, double OffsetY, double Angle);

// ── Parse .sparam blocked format ──

static List<SparamBlock> ParseSparamFile(string path)
{
    var blocks = new List<SparamBlock>();
    var lines = File.ReadAllLines(path);
    int i = 0;

    while (i < lines.Length)
    {
        var line = lines[i].Trim();
        if (line.StartsWith("(") && line.Contains("transmission"))
        {
            // Parse header: ('port X','MODE',mode_id,'port Y',mode_id,'transmission')
            var match = Regex.Match(line, @"\('([^']+)','?(\w+)'?,\d+,'([^']+)',\d+,'transmission'\)");
            if (!match.Success)
            {
                // Try double-quote variant
                match = Regex.Match(line, @"\(""([^""]+)"",""?([^"",]+)""?,\d+,""([^""]+)"",\d+,""transmission""\)");
            }

            if (match.Success)
            {
                var outPort = match.Groups[1].Value;
                var mode = match.Groups[2].Value;
                var inPort = match.Groups[3].Value;

                i++;
                // Parse shape: (N,3)
                var shapeLine = lines[i].Trim();
                var shapeMatch = Regex.Match(shapeLine, @"\((\d+)\s*,\s*3\)");
                int numPoints = shapeMatch.Success ? int.Parse(shapeMatch.Groups[1].Value) : 0;

                var data = new List<(double, double, double)>();
                for (int j = 0; j < numPoints && i + 1 + j < lines.Length; j++)
                {
                    var dataLine = lines[i + 1 + j].Trim();
                    var parts = dataLine.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var freq = double.Parse(parts[0], CultureInfo.InvariantCulture);
                        var mag = double.Parse(parts[1], CultureInfo.InvariantCulture);
                        var phase = double.Parse(parts[2], CultureInfo.InvariantCulture);
                        data.Add((freq, mag, phase));
                    }
                }

                blocks.Add(new SparamBlock(outPort, inPort, mode, data));
                i += 1 + numPoints;
            }
            else
            {
                i++;
            }
        }
        else
        {
            i++;
        }
    }

    return blocks;
}

// ── Parse GC .txt packed format ──

static List<SparamBlock> ParseGcTxtFile(string path)
{
    var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
    var s11 = new List<(double, double, double)>();
    var s21 = new List<(double, double, double)>();
    var s12 = new List<(double, double, double)>();
    var s22 = new List<(double, double, double)>();

    foreach (var line in lines)
    {
        var parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 9)
        {
            var freq = double.Parse(parts[0], CultureInfo.InvariantCulture);
            s11.Add((freq, double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture)));
            s21.Add((freq, double.Parse(parts[3], CultureInfo.InvariantCulture), double.Parse(parts[4], CultureInfo.InvariantCulture)));
            s12.Add((freq, double.Parse(parts[5], CultureInfo.InvariantCulture), double.Parse(parts[6], CultureInfo.InvariantCulture)));
            s22.Add((freq, double.Parse(parts[7], CultureInfo.InvariantCulture), double.Parse(parts[8], CultureInfo.InvariantCulture)));
        }
    }

    return new List<SparamBlock>
    {
        new("port 1", "port 1", "TE", s11),
        new("port 2", "port 1", "TE", s21),
        new("port 1", "port 2", "TE", s12),
        new("port 2", "port 2", "TE", s22)
    };
}

// ── Convert frequency to wavelength ──

static int FreqToWavelengthNm(double freqHz) =>
    (int)Math.Round(SpeedOfLight / freqHz * 1e9);

// ── Sample blocks at specific wavelengths ──

static Dictionary<int, List<Dictionary<string, object>>> SampleAtWavelengths(
    List<SparamBlock> blocks, int[] targetWavelengths, string modeFilter = "TE")
{
    var result = new Dictionary<int, List<Dictionary<string, object>>>();

    var teBlocks = blocks.Where(b => b.Mode.Equals(modeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
    if (teBlocks.Count == 0) return result;

    // Get all unique wavelengths from first block
    var allWavelengths = teBlocks[0].Data.Select(d => FreqToWavelengthNm(d.FreqHz)).ToArray();

    foreach (var targetNm in targetWavelengths)
    {
        // Find nearest frequency index
        int bestIdx = 0;
        int bestDiff = int.MaxValue;
        for (int i = 0; i < allWavelengths.Length; i++)
        {
            var diff = Math.Abs(allWavelengths[i] - targetNm);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIdx = i;
            }
        }

        var connections = new List<Dictionary<string, object>>();
        foreach (var block in teBlocks)
        {
            if (bestIdx < block.Data.Count)
            {
                var (_, mag, phaseRad) = block.Data[bestIdx];
                var phaseDeg = phaseRad * 180.0 / Math.PI;
                connections.Add(new Dictionary<string, object>
                {
                    ["fromPin"] = block.InPort,
                    ["toPin"] = block.OutPort,
                    ["magnitude"] = Math.Round(mag, 6),
                    ["phaseDegrees"] = Math.Round(phaseDeg, 2)
                });
            }
        }

        result[allWavelengths[bestIdx]] = connections;
    }

    return result;
}

// ── Component definitions ──

var components = new List<ComponentSpec>
{
    new("Y-Branch 1550", "Splitters", "ebeam_y_1550", "",
        10, 12,
        new[] {
            new PinSpec("port 1", 0, 6, 180),
            new PinSpec("port 2", 10, 3, 0),
            new PinSpec("port 3", 10, 9, 0)
        },
        "y_branch.sparam", "sparam"),

    new("Directional Coupler TE 1550", "Couplers", "ebeam_dc_te1550", "gap=200e-9",
        30, 12,
        new[] {
            new PinSpec("port 1", 0, 3, 180),
            new PinSpec("port 2", 0, 9, 180),
            new PinSpec("port 3", 30, 9, 0),
            new PinSpec("port 4", 30, 3, 0)
        },
        "dc_te1550.sparam", "sparam"),

    new("Grating Coupler TE 1550", "I/O", "ebeam_gc_te1550", "",
        30, 30,
        new[] {
            new PinSpec("port 1", 15, 30, 90),
            new PinSpec("port 2", 30, 15, 0)
        },
        "gc_te1550.txt", "gc_txt"),

    new("Terminator TE 1550", "Termination", "ebeam_terminator_te1550", "",
        10, 5,
        new[] {
            new PinSpec("port 1", 0, 2.5, 180)
        },
        "terminator_te1550.sparam", "sparam")
};

// ── Main conversion ──

var targetWavelengths = new[] { 1500, 1510, 1520, 1530, 1540, 1550, 1560, 1570, 1580, 1590, 1600 };
var scriptDir = Path.GetDirectoryName(Environment.GetCommandLineArgs().FirstOrDefault()) ?? ".";
var dataDir = Path.Combine(scriptDir, "sparam-data");
if (!Directory.Exists(dataDir))
    dataDir = "Tools/sparam-data"; // fallback

var pdkComponents = new List<object>();

foreach (var spec in components)
{
    var filePath = Path.Combine(dataDir, spec.SourceFile);
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"  SKIP: {spec.SourceFile} not found at {filePath}");
        continue;
    }

    Console.WriteLine($"Processing {spec.Name} from {spec.SourceFile}...");

    var blocks = spec.Format == "gc_txt"
        ? ParseGcTxtFile(filePath)
        : ParseSparamFile(filePath);

    Console.WriteLine($"  Parsed {blocks.Count} S-parameter blocks");

    var sampled = SampleAtWavelengths(blocks, targetWavelengths);
    Console.WriteLine($"  Sampled at {sampled.Count} wavelengths");

    // Build wavelengthData array
    var wavelengthData = sampled
        .OrderBy(kv => kv.Key)
        .Select(kv => new Dictionary<string, object>
        {
            ["wavelengthNm"] = kv.Key,
            ["connections"] = kv.Value
        })
        .ToList();

    // Also set single-wavelength connections at 1550nm for backward compat
    var conn1550 = sampled.OrderBy(kv => Math.Abs(kv.Key - 1550)).FirstOrDefault().Value
        ?? new List<Dictionary<string, object>>();

    pdkComponents.Add(new Dictionary<string, object>
    {
        ["name"] = spec.Name,
        ["category"] = spec.Category,
        ["nazcaFunction"] = spec.NazcaFunction,
        ["nazcaParameters"] = spec.NazcaParameters,
        ["widthMicrometers"] = spec.WidthUm,
        ["heightMicrometers"] = spec.HeightUm,
        ["pins"] = spec.Pins.Select(p => new Dictionary<string, object>
        {
            ["name"] = p.Name,
            ["offsetXMicrometers"] = p.OffsetX,
            ["offsetYMicrometers"] = p.OffsetY,
            ["angleDegrees"] = p.Angle
        }).ToList(),
        ["sMatrix"] = new Dictionary<string, object>
        {
            ["wavelengthNm"] = 1550,
            ["connections"] = conn1550,
            ["wavelengthData"] = wavelengthData
        }
    });
}

var pdk = new Dictionary<string, object>
{
    ["fileFormatVersion"] = 1,
    ["name"] = "SiEPIC EBeam PDK",
    ["description"] = "UBC SiEPIC EBeam PDK (SOI 220nm) - S-parameters from Lumerical simulations",
    ["foundry"] = "UBC / SiEPIC",
    ["version"] = "0.5.4",
    ["defaultWavelengthNm"] = 1550,
    ["nazcaModuleName"] = "siepic_ebeam_pdk",
    ["components"] = pdkComponents
};

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var json = JsonSerializer.Serialize(pdk, options);
var outputPath = Path.Combine(scriptDir, "..", "CAP-DataAccess", "PDKs", "siepic-ebeam-pdk.json");
if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
    outputPath = "CAP-DataAccess/PDKs/siepic-ebeam-pdk.json";

File.WriteAllText(outputPath, json);
Console.WriteLine($"\nWrote PDK to: {Path.GetFullPath(outputPath)}");
Console.WriteLine($"Components: {pdkComponents.Count}");
