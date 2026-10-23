using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Tiles;

namespace UnitTests.Services;

/// <summary>Fixed synthetic cells for complete Nazca script contracts; no external PDK or files.</summary>
internal static class NazcaGoldenScriptFixture
{
    internal static Component Cell(string id, string function, double x, double y, double width, double height)
    {
        var component = TestComponentFactory.CreatePinlessComponent(x, y, width, height);
        component.Identifier = id;
        component.NazcaFunctionName = function;
        return component;
    }

    internal static PhysicalPin AddPin(
        Component component, string name, double x, double y, double angle, MatterType matter)
    {
        var pin = new PhysicalPin
        {
            Name = name,
            ParentComponent = component,
            OffsetXMicrometers = x,
            OffsetYMicrometers = y,
            AngleDegrees = angle,
            LogicalPin = new Pin(name, 0, matter, RectSide.Right),
        };
        component.PhysicalPins.Add(pin);
        return pin;
    }
}
