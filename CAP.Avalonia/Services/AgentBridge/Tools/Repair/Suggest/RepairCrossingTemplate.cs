using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Routing.Snapshots;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Captures the manual cut supply before awaiting; no live template or factory survives capture.</summary>
internal sealed record RepairCrossingTemplate(string Name, string PdkSource, string? Function,
    double Width, double Height, RoutingComponentPrototype Prototype)
{
    internal static RepairCrossingTemplate? Capture(AgentDesignSession session)
    {
        var template = CrossingComponentInstance.FindCrossingTemplate(session.Templates);
        if (template == null) return null;
        var prototype = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var components = session.Canvas.Components.Select(view => view.Component)
            .SelectMany(component => component is CAP_Core.Components.Core.ComponentGroup group
                ? group.GetAllComponentsRecursive() : (IEnumerable<CAP_Core.Components.Core.Component>)new[] { component });
        return new(template.Name, template.PdkSource, template.NazcaFunctionName,
            template.WidthMicrometers, template.HeightMicrometers,
            RoutingComponentPrototype.Capture(prototype, components, session.Canvas.Router));
    }

    internal ComponentTemplate Bind(RoutingWorkGraph graph) => new()
    {
        Name = Name, PdkSource = PdkSource, NazcaFunctionName = Function,
        WidthMicrometers = Width, HeightMicrometers = Height,
        DetachedComponentFactory = () => graph.CreateComponent(Prototype)
    };
}
