using System.Text.Json;
using CAP_Core.Export;
using CAP_Core.Export.Netlist;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Exchange;

/// <summary>
/// <c>export_netlist</c>: writes the design as a circuit netlist other tools consume —
/// gdsfactory YAML, a SAX circuit script, a PhotonTorch script, or Verilog-A modules with a
/// SPICE test bench — to a file or folder, or returns it inline.
/// </summary>
public sealed class ExportNetlistTool : IAgentBridgeTool
{
    private const string GdsFactoryYaml = "gdsfactory_yaml";
    private const string Sax = "sax";
    private const string PhotonTorch = "photontorch";
    private const string VerilogA = "verilog_a";
    private const int DefaultWavelengthNm = 1550;
    private const int MaxInlineCharacters = 60_000;

    private readonly AgentDesignSession _session;
    private readonly SaxExporter _sax;
    private readonly PhotonTorchExporter _photonTorch;

    /// <summary>Creates the tool.</summary>
    public ExportNetlistTool(AgentDesignSession session, SaxExporter sax, PhotonTorchExporter photonTorch)
    {
        _session = session;
        _sax = sax;
        _photonTorch = photonTorch;
    }

    /// <inheritdoc/>
    public string Name => "export_netlist";

    /// <inheritdoc/>
    public string Description =>
        "Export the circuit as a netlist: format gdsfactory_yaml (instances, placements, connections, ports), " +
        "sax (Python circuit simulation script), photontorch (Python script) or verilog_a (one module per component + " +
        "top-level netlist + SPICE test bench, written into a folder). Pass path to write it, otherwise the text is returned.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            format = new { type = "string", @enum = new[] { GdsFactoryYaml, Sax, PhotonTorch, VerilogA } },
            path = new { type = "string", description = "Output file (folder for verilog_a). Omit to return the text." },
            wavelength_nm = new { type = "integer", description = $"Design wavelength (default {DefaultWavelengthNm})." },
        },
        required = new[] { "format" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var components = _session.Canvas.Components.Select(c => c.Component).ToList();
        if (components.Count == 0)
            return AgentToolResult.Error("The design is empty.");
        var connections = _session.Canvas.Connections.Select(c => c.Connection).ToList();
        var format = ToolArguments.GetString(arguments, "format");
        var path = ToolArguments.GetString(arguments, "path");
        var wavelength = (int)(ToolArguments.GetDouble(arguments, "wavelength_nm") ?? DefaultWavelengthNm);
        var name = DesignName();

        if (format == VerilogA)
            return await ExportVerilogAAsync(components, connections, wavelength, name, path);

        string? text = format switch
        {
            GdsFactoryYaml => new GdsFactoryYamlNetlistWriter().Write(new NetlistDeriver().Derive(components, connections, name)),
            Sax => _sax.Export(components, connections, wavelength),
            PhotonTorch => _photonTorch.Export(components, connections),
            _ => null,
        };
        if (text == null)
            return AgentToolResult.Error($"format must be one of {GdsFactoryYaml}, {Sax}, {PhotonTorch}, {VerilogA}.");
        if (path == null)
            return text.Length <= MaxInlineCharacters
                ? AgentToolResult.Text(text)
                : AgentToolResult.Error($"The netlist has {text.Length} characters; pass a path to write it to a file.");

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, text);
        return AgentToolResult.Json(new { Format = format, Path = fullPath, Characters = text.Length });
    }

    private static async Task<AgentToolResult> ExportVerilogAAsync(
        IReadOnlyList<CAP_Core.Components.Core.Component> components,
        IReadOnlyList<CAP_Core.Components.Connections.WaveguideConnection> connections,
        int wavelength, string name, string? folder)
    {
        if (folder == null)
            return AgentToolResult.Error("verilog_a writes several files: pass path (a folder).");
        var result = new VerilogAExporter().Export(components, connections,
            new VerilogAExportOptions { WavelengthNm = wavelength, CircuitName = SafeName(name) });
        if (!result.Success)
            return AgentToolResult.Error($"Verilog-A export failed: {result.ErrorMessage}");

        var directory = Path.GetFullPath(folder);
        Directory.CreateDirectory(directory);
        foreach (var (file, content) in result.ComponentFiles)
            await File.WriteAllTextAsync(Path.Combine(directory, file), content);
        var top = Path.Combine(directory, result.CircuitName + ".va");
        await File.WriteAllTextAsync(top, result.TopLevelNetlist);
        var bench = Path.Combine(directory, result.CircuitName + "_tb.sp");
        await File.WriteAllTextAsync(bench, result.SpiceTestBench);
        return AgentToolResult.Json(new
        {
            Format = VerilogA,
            Folder = directory,
            TopLevel = top,
            TestBench = bench,
            ComponentFiles = result.ComponentFiles.Keys.ToList(),
        });
    }

    private string DesignName()
    {
        var file = _session.FileOperations?.CurrentFilePath;
        return file != null ? Path.GetFileNameWithoutExtension(file) : "lunima_design";
    }

    private static string SafeName(string name) =>
        new(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
