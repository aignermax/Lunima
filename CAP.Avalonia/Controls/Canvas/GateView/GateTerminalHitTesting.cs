using Avalonia;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>A displayed gate terminal and the physical port it represents.</summary>
internal sealed record GateTerminal(PhysicalPin Pin, Point Position, bool IsOutput);

/// <summary>Resolves only visible schematic terminals, never nearby hidden physical ports.</summary>
internal static class GateTerminalHitTesting
{
    internal static GateTerminal? HitTest(GateViewScene scene, Point point, double zoom)
    {
        var radius = 8 / Math.Max(zoom, 0.001);
        GateTerminal? nearest = null;
        var distance = radius * radius;
        foreach (var symbol in scene.Symbols)
        {
            Check(symbol, false);
            Check(symbol, true);
        }
        return nearest;

        void Check(GateSymbolPlacement symbol, bool output)
        {
            var ports = output ? symbol.Node.Outputs : symbol.Node.Inputs;
            var positions = output ? symbol.OutputPins : symbol.InputPins;
            for (var i = 0; i < ports.Count; i++)
            {
                var offset = positions[i] - point;
                var squared = offset.X * offset.X + offset.Y * offset.Y;
                if (squared > distance) continue;
                var name = ports[i].Name;
                var pin = symbol.Node.Group.ExternalPins.FirstOrDefault(p => p.Name == name)?.InternalPin
                    ?? symbol.Node.Group.PhysicalPins.FirstOrDefault(p => p.Name == name);
                if (pin == null) continue;
                nearest = new GateTerminal(pin, positions[i], output);
                distance = squared;
            }
        }
    }
}
