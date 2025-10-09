// CAP_Core/CodeExporter/NazcaExporter.cs
using CAP_Contracts;
using CAP_Core.Components;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.Helpers;
using CAP_Core.Tiles;
using System.Text;
using System.Globalization;

namespace CAP_Core.CodeExporter
{
    
    public class NazcaExporter : IExporter
    {
        private GridManager? grid;
        private List<Component>? AlreadyProcessedComponents = new();

        // NEU: Flag für Export-Modus
        public bool UsePhysicalCoordinates { get; set; } = false;

        public string Export(GridManager grid)
        {
            this.grid = grid;
            UsePhysicalCoordinates = grid.UsePhysicalCoordinates;
            AlreadyProcessedComponents = new List<Component>();

            StringBuilder NazcaCode = new();
            NazcaCode.Append(CreateHeader());

            if (UsePhysicalCoordinates)
            {
                AddComponentsPhysicalMode(NazcaCode);
            }
            else
            {
                AddComponentsConnectedToStandardInputs(NazcaCode);
                AddOrphans(NazcaCode);
            }

            NazcaCode.Append(PythonResources.CreateFooter());
            return NazcaCode.ToString();
        }

        // NEU: Physikalischer Export-Modus
        private void AddComponentsPhysicalMode(StringBuilder NazcaCode)
        {
            var allComponents = grid.TileManager.GetAllComponents();

            // 1. Erst alle Komponenten mit absoluten Koordinaten platzieren
            foreach (var component in allComponents)
            {
                NazcaCode.Append(ExportComponentPhysical(component));
                AlreadyProcessedComponents.Add(component);
            }

            // 2. Dann alle Waveguide-Connections
            NazcaCode.Append(ExportWaveguideConnections());

            // 3. External Ports (Grating Couplers) verbinden
            NazcaCode.Append(ExportExternalPortConnections());
        }

        private string ExportComponentPhysical(Component component)
        {
            var cellName = GetPhysicalCellName(component);
            var parameters = component.NazcaFunctionParameters;

            // Koordinaten in µm (mit Invariant Culture für Punkt statt Komma)
            var posX = component.PhysicalX.ToString("F3", CultureInfo.InvariantCulture);
            var posY = component.PhysicalY.ToString("F3", CultureInfo.InvariantCulture);
            var rotation = component.RotationDegrees.ToString("F1", CultureInfo.InvariantCulture);

            return $"        {cellName} = CAPICPDK.{component.NazcaFunctionName}({parameters})" +
                   $".put({posX}, {posY}, {rotation})\n";
        }

        private string GetPhysicalCellName(Component component)
        {
            // Eindeutiger Name basierend auf Component-Identifier oder GUID
            return $"comp_{component.Identifier}_{component.GetHashCode():X8}";
        }

        private string ExportWaveguideConnections()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n        # Waveguide Connections");
            sb.AppendLine("        ic = Interconnect(width=1.2, radius=80)");

            foreach (var connection in grid.WaveguideConnections.Connections)
            {
                sb.Append(ExportSingleWaveguide(connection));
            }

            return sb.ToString();
        }

        private string ExportSingleWaveguide(WaveguideConnection connection)
        {
            var startCell = GetPhysicalCellName(connection.StartPin.ParentComponent);
            var endCell = GetPhysicalCellName(connection.EndPin.ParentComponent);
            var startPin = connection.StartPin.Name;
            var endPin = connection.EndPin.Name;

            return connection.Type switch
            {
                WaveguideType.Straight =>
                    $"        ic.strt_p2p(pin1={startCell}.pin['{startPin}'], " +
                    $"pin2={endCell}.pin['{endPin}']).put()\n",

                WaveguideType.SBend =>
                    $"        ic.sbend_p2p(pin1={startCell}.pin['{startPin}'], " +
                    $"pin2={endCell}.pin['{endPin}'], " +
                    $"radius={connection.BendRadiusMicrometers.ToString(CultureInfo.InvariantCulture)}).put()\n",

                _ => // Auto: cobra_p2p
                    $"        ic.cobra_p2p(pin1={startCell}.pin['{startPin}'], " +
                    $"pin2={endCell}.pin['{endPin}']).put()\n"
            };
        }

        private string ExportExternalPortConnections()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n        # External Port Connections");

            foreach (var usedInput in grid.ExternalPortManager.GetUsedExternalInputs())
            {
                var input = usedInput.Input;
                var gratingName = input.IsLeftPort ? "grating1" : "grating2";

                // Finde die verbundene Komponente
                var x = input.IsLeftPort ? 0 : grid.TileManager.Width - 1;
                var y = input.TilePositionY;

                if (!grid.TileManager.IsCoordinatesInGrid(x, y, 1, 1)) continue;
                var connectedComponent = grid.ComponentMover.GetComponentAt(x, y);
                if (connectedComponent == null) continue;

                var componentCell = GetPhysicalCellName(connectedComponent);
                var componentPinSide = input.IsLeftPort ? RectSide.Left : RectSide.Right;
                var componentPin = connectedComponent.GetPinAt(x, y, componentPinSide);

                if (componentPin != null)
                {
                    sb.AppendLine(
                        $"        ic.cobra_p2p(pin1={gratingName}.pin['{input.PinName}'], " +
                        $"pin2={componentCell}.pin['{componentPin.Name}']).put()");
                }
            }

            return sb.ToString();
        }

        private string CreateHeader()
        {
            if (UsePhysicalCoordinates)
            {
                return CreatePhysicalHeader();
            }
            return CreateGridHeader(); // NEU: Separate Methode für Grid-Header
        }

        private string CreateGridHeader()
        {
            var numIO = CountExternalPorts();
            var gridWidth = grid.TileManager.Width;

            return $@"import nazca as nd
from TestPDK import TestPDK
CAPICPDK = TestPDK()

def FullDesign(layoutName):
    with nd.Cell(name=layoutName) as fullLayoutInner:
        grating1 = CAPICPDK.placeGratingArray_East({numIO}).put(0, 0)
        grating2 = CAPICPDK.placeGratingArray_West({numIO}).put(CAPICPDK._CellSize * {gridWidth}, 0)
        
        # Grid-based components
";
        }

        private string CreatePhysicalHeader()
        {
            var numIO = CountExternalPorts();
            var chipWidthMicrometers = CalculateChipWidth(); // z.B. 6000 µm

            return $@"import nazca as nd
from TestPDK import TestPDK
from nazca.interconnects import Interconnect

CAPICPDK = TestPDK()

def FullDesign(layoutName):
    with nd.Cell(name=layoutName) as fullLayoutInner:
        # Grating Coupler Arrays
        grating1 = CAPICPDK.placeGratingArray_East({numIO}).put(0, 0)
        grating2 = CAPICPDK.placeGratingArray_West({numIO}).put({chipWidthMicrometers}, 0)
        
        # Interconnect for automatic waveguide routing
        ic = Interconnect(width=CAPICPDK._WGWidth, radius=CAPICPDK._BendRadius)
        
        # Physical components
";
        }

        
        private int CountExternalPorts()
        {
            return grid.ExternalPortManager.ExternalPorts
                .Where(p => p.IsLeftPort)
                .Count();
        }

        private double CalculateChipWidth()
        {
            // Im Physical-Modus: Finde die am weitesten rechts liegende Komponente
            if (UsePhysicalCoordinates)
            {
                var allComponents = grid.TileManager.GetAllComponents();
                if (allComponents.Any())
                {
                    var maxX = allComponents.Max(c => c.PhysicalX + c.WidthMicrometers);
                    return Math.Ceiling(maxX / 1000) * 1000; // Runde auf nächste 1000 µm
                }
                return 6000; // Default: 6mm
            }

            // Im Grid-Modus: Grid Width * Cell Size
            return grid.TileManager.Width * Tile.GridToMicrometerScale;
        }
        // ========== ALTE GRID-BASIERTE METHODEN (bleiben für Backward-Kompatibilität) ==========

        private StringBuilder ExportAllConnectedTiles(Tile connectedParent, Tile child)
        {
            if (AlreadyProcessedComponents == null)
                throw new NullReferenceException($"The list of {nameof(AlreadyProcessedComponents)} cannot be null");
            if (child.Component == null)
                throw new NullReferenceException($"child.{nameof(child.Component)} cannot be null");

            var nazcaString = new StringBuilder();
            nazcaString.Append(child.ExportToNazca(connectedParent));
            AlreadyProcessedComponents.Add(child.Component);

            var neighbors = GetUnComputedNeighbors(child);
            foreach (ParentAndChildTile childNeighborTile in neighbors)
            {
                if (AlreadyProcessedComponents.Contains(childNeighborTile.Child.Component)) continue;
                nazcaString.Append(ExportAllConnectedTiles(childNeighborTile.ParentPart, childNeighborTile.Child));
            }
            return nazcaString;
        }

        private void AddComponentsConnectedToStandardInputs(StringBuilder NazcaCode)
        {
            foreach (ExternalPort port in grid.ExternalPortManager.ExternalPorts)
            {
                var x = port.IsLeftPort ? 0 : grid.TileManager.Width - 1;
                var y = port.TilePositionY;
                if (!grid.TileManager.IsCoordinatesInGrid(x, y, 1, 1)) continue;

                var firstConnectedTile = grid.TileManager.Tiles[x, y];
                if (firstConnectedTile.Component == null) continue;
                if (firstConnectedTile.GetPinAt(port.IsLeftPort ? RectSide.Left : RectSide.Right)?.MatterType != MatterType.Light) continue;
                if (AlreadyProcessedComponents.Contains(firstConnectedTile.Component)) continue;

                StartConnectingAtPorts(NazcaCode, port, firstConnectedTile);
            }
        }

        private void AddOrphans(StringBuilder NazcaCode)
        {
            for (int x = 0; x < grid.TileManager.Width; x++)
            {
                for (int y = 0; y < grid.TileManager.Height; y++)
                {
                    var comp = grid.TileManager.Tiles[x, y].Component;
                    if (comp == null) continue;
                    if (AlreadyProcessedComponents.Contains(comp)) continue;
                    StartConnectingAtTile(NazcaCode, grid.TileManager.Tiles[x, y]);
                }
            }
        }

        private void StartConnectingAtTile(StringBuilder NazcaCode, Tile currentTile)
        {
            NazcaCode.Append(currentTile.ExportToNazcaAbsolutePosition());
            AlreadyProcessedComponents.Add(currentTile.Component);
            ExportAllNeighbors(NazcaCode, currentTile);
        }

        private void ExportAllNeighbors(StringBuilder NazcaCode, Tile currentTile)
        {
            List<ParentAndChildTile> neighbors = grid.ComponentRelationshipManager
                .GetConnectedNeighborsOfComponent(currentTile.Component);
            if (neighbors != null)
            {
                foreach (ParentAndChildTile neighbor in neighbors)
                {
                    if (AlreadyProcessedComponents.Contains(neighbor.Child.Component)) continue;
                    NazcaCode.Append(ExportAllConnectedTiles(neighbor.ParentPart, neighbor.Child));
                }
            }
        }

        private List<ParentAndChildTile> GetUnComputedNeighbors(Tile currentTile)
        {
            var neighbors = grid.ComponentRelationshipManager
                .GetConnectedNeighborsOfComponent(currentTile.Component);
            return neighbors.Where(n => !AlreadyProcessedComponents.Contains(n.Child.Component)).ToList();
        }

        private void StartConnectingAtPorts(StringBuilder NazcaCode, ExternalPort input, Tile firstConnectedTile)
        {
            NazcaCode.Append(firstConnectedTile.ExportToNazcaExtended(
                new IntVector(input.IsLeftPort ? -1 : grid.TileManager.Width, input.TilePositionY),
                input.IsLeftPort ? Resources.NazcaStandardLeftInputCellName : Resources.NazcaStandardRightInputCellName,
                input.PinName));
            AlreadyProcessedComponents.Add(firstConnectedTile.Component);
            ExportAllNeighbors(NazcaCode, firstConnectedTile);
        }
    }
}
