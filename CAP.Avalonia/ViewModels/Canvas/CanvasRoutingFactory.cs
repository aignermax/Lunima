using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Documents;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>Constructs the canvas routing owner while preserving its shared models and document lifetime.</summary>
internal delegate RoutingOrchestrator CanvasRoutingFactory(WaveguideRouter router,
    WaveguideConnectionManager manager, ObservableCollection<ComponentViewModel> components,
    ObservableCollection<WaveguideConnectionViewModel> connections, DocumentSession document);
