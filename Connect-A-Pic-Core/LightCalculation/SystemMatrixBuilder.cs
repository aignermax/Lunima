using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Grid;
using CAP_Core.Helpers;
using CAP_Core.Tiles;
using System.Numerics;
namespace CAP_Core.LightCalculation
{
    public interface ISystemMatrixBuilder
    {
        public SMatrix GetSystemSMatrix(int LaserWaveLengthInNm);
    }
    public class  SystemMatrixBuilder : ISystemMatrixBuilder
    {
        public GridManager Grid { get; set; }
        public SystemMatrixBuilder(GridManager grid)
        {
            Grid = grid;
        }
        public SMatrix GetSystemSMatrix(int LaserWaveLengthInNm)
        {
            var allComponentsSMatrices = GetAllComponentsSMatrices(LaserWaveLengthInNm);
            SMatrix allConnectionsSMatrix = Grid.UsePhysicalCoordinates
               ? CreatePhysicalConnectionsMatrix()
               : CreateInterComponentsConnectionsMatrix();
            allComponentsSMatrices.Add(allConnectionsSMatrix);
            return SMatrix.CreateSystemSMatrix(allComponentsSMatrices);
        }
        private SMatrix CreatePhysicalConnectionsMatrix()
        {
            var connections = Grid.WaveguideConnections.GetConnectionTransfers();

            // Also include frozen internal paths from ComponentGroups so that grouped
            // components are treated identically to flat components during simulation.
            foreach (var frozenTransfer in GetAllFrozenPathTransfers())
            {
                connections[frozenTransfer.Key] = frozenTransfer.Value;
            }

            var pinIdSet = new HashSet<Guid>(
                connections.SelectMany(c => new[] { c.Key.Item1, c.Key.Item2 }));

            foreach (var input in Grid.ExternalPortManager.GetUsedExternalInputs())
            {
                pinIdSet.Add(input.AttachedComponentPinId);
            }

            var connectionsSMatrix = new SMatrix(pinIdSet.ToList(), new());
            connectionsSMatrix.SetValues(connections);
            return connectionsSMatrix;
        }

        /// <summary>
        /// Collects connection transfers from all FrozenWaveguidePaths inside every
        /// ComponentGroup that is present in the tile manager (recursively).
        /// These transfers replace the group's pre-computed transitive S-matrix so that
        /// the outer iterative simulation sees individual component matrices and explicit
        /// connections — exactly as it does for a flat (ungrouped) circuit.
        /// </summary>
        private Dictionary<(Guid, Guid), Complex> GetAllFrozenPathTransfers()
        {
            var transfers = new Dictionary<(Guid, Guid), Complex>();
            foreach (var component in Grid.TileManager.GetAllComponents())
            {
                if (component is ComponentGroup group)
                {
                    CollectFrozenPathTransfers(group, transfers);
                }
            }
            return transfers;
        }

        private static void CollectFrozenPathTransfers(
            ComponentGroup group,
            Dictionary<(Guid, Guid), Complex> transfers)
        {
            foreach (var path in group.InternalPaths)
            {
                if (path.StartPin?.LogicalPin == null || path.EndPin?.LogicalPin == null)
                    continue;

                var coeff = path.TransmissionCoefficient;
                // Forward: StartPin.OutFlow → EndPin.InFlow
                transfers[(path.StartPin.LogicalPin.IDOutFlow, path.EndPin.LogicalPin.IDInFlow)] = coeff;
                // Reverse: EndPin.OutFlow → StartPin.InFlow (waveguides are bidirectional)
                transfers[(path.EndPin.LogicalPin.IDOutFlow, path.StartPin.LogicalPin.IDInFlow)] = coeff;
            }

            // Recurse into nested groups
            foreach (var child in group.ChildComponents)
            {
                if (child is ComponentGroup nestedGroup)
                    CollectFrozenPathTransfers(nestedGroup, transfers);
            }
        }

        private SMatrix CreateInterComponentsConnectionsMatrix()
        {
            var interComponentConnections = GetAllConnectionsBetweenComponents();
            var allUsedPinIDs = interComponentConnections.SelectMany(c => new[] { c.Key.Item1, c.Key.Item2 }).Distinct().ToList();
            foreach( var input in Grid.ExternalPortManager.GetUsedExternalInputs())
            {
                allUsedPinIDs.Add(input.AttachedComponentPinId); // Grating coupler has no internal connections and might be only connected to the Laser directly
            }
            var allConnectionsSMatrix = new SMatrix(allUsedPinIDs, new());
            allConnectionsSMatrix.SetValues(interComponentConnections);
            return allConnectionsSMatrix;
        }
        private List<SMatrix> GetAllComponentsSMatrices(int waveLength)
        {
            var allComponents = Grid.TileManager.GetAllComponents();
            var allSMatrices = new List<SMatrix>();
            foreach (var component in allComponents)
            {
                // ComponentGroups: flatten transparently by using each child's individual
                // S-matrix rather than the group's pre-computed transitive S-matrix.
                // Internal connections are captured separately via GetAllFrozenPathTransfers().
                // This prevents double-counting: the transitive group matrix already encodes
                // multi-hop transfer, so embedding it inside the outer iterative simulation
                // (which converges the same Neumann series) would apply the internal chain twice.
                if (component is ComponentGroup group)
                {
                    // Still compute the group S-matrix for external consumers (e.g. serialization,
                    // ParameterSweeper) that read WaveLengthToSMatrixMap directly.
                    if (group.WaveLengthToSMatrixMap.Count == 0)
                        group.EnsureSMatrixComputed();

                    CollectChildSMatrices(group, waveLength, allSMatrices);
                    continue;
                }

                AddComponentSMatrix(component, waveLength, allSMatrices);
            }
            return allSMatrices;
        }

        private static void CollectChildSMatrices(
            ComponentGroup group, int waveLength, List<SMatrix> result)
        {
            foreach (var child in group.ChildComponents)
            {
                if (child is ComponentGroup nestedGroup)
                {
                    CollectChildSMatrices(nestedGroup, waveLength, result);
                }
                else
                {
                    AddComponentSMatrix(child, waveLength, result);
                }
            }
        }

        private static void AddComponentSMatrix(
            Component component, int waveLength, List<SMatrix> result)
        {
            if (component.WaveLengthToSMatrixMap.TryGetValue(waveLength, out var matrixFound))
            {
                result.Add(matrixFound);
            }
            else if (component.WaveLengthToSMatrixMap.Count > 0)
            {
                // Nearest-wavelength fallback for multi-wavelength PDK components
                var nearestKey = component.WaveLengthToSMatrixMap.Keys
                    .OrderBy(k => Math.Abs(k - waveLength))
                    .First();
                result.Add(component.WaveLengthToSMatrixMap[nearestKey]);
            }
            else
            {
                throw new InvalidDataException(
                    $"The Matrix was not defined for the specific waveLength: {waveLength} " +
                    $"at component {component.Identifier}");
            }
        }
        private Dictionary<(Guid LightIn, Guid LightOut), Complex> GetAllConnectionsBetweenComponents()
        {
            int gridWidth = Grid.TileManager.Tiles.GetLength(0);
            int gridHeight = Grid.TileManager.Tiles.GetLength(1);
            var InterComponentConnections = new Dictionary<(Guid LightIn, Guid LightOut), Complex>();

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    GatherConnectionsAtAllComponentBordersAtTile(x, y, InterComponentConnections);
                }
            }
            return InterComponentConnections;
        }

        private void GatherConnectionsAtAllComponentBordersAtTile(int x, int y, Dictionary<(Guid LightIn, Guid LightOut), Complex> InterComponentConnections)
        {
            Array allSides = Enum.GetValues(typeof(RectSide));
            foreach (RectSide side in allSides)
            {
                IntVector offset = side; // transforming the side to a vector that points towards the side
                if (Grid.TileManager.Tiles[x, y].Component == null) continue;
                if (!Grid.TileManager.IsCoordinatesInGrid(x + offset.X, y + offset.Y)) continue;
                var foreignTile = Grid.TileManager.Tiles[x + offset.X, y + offset.Y];
                if (!IsComponentBorderEdge(x, y, foreignTile)) continue;
                Pin currentPin = Grid.TileManager.Tiles[x, y].GetPinAt(side);
                if (currentPin == null) continue;
                var foreignPinSide = offset * -1;
                Pin foreignPin = foreignTile.GetPinAt(foreignPinSide);
                if (foreignPin == null) continue;

                InterComponentConnections.Add((currentPin.IDOutFlow, foreignPin.IDInFlow), 1);
            }
        }

        private bool IsComponentBorderEdge(int gridX, int gridY, Tile foreignTile)
        {
            if (foreignTile == null) return false;
            var centeredComponent = Grid.TileManager.Tiles[gridX, gridY].Component;
            return centeredComponent != foreignTile.Component;
        }
    }
}
