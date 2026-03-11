using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Tiles;
using System.Numerics;
using CAP_Core.LightCalculation;

namespace UnitTests
{
    /// <summary>
    /// Factory for creating test components without external dependencies.
    /// </summary>
    public class TestComponentFactory
    {
        public static Component CreateStraightWaveGuide()
        {
            int widthInTiles = 1;
            int heightInTiles = 1;

            Part[,] parts = new Part[widthInTiles, heightInTiles];

            parts[0, 0] = new Part(new List<Pin>() {
                new ("west0",0, MatterType.Light, RectSide.Left),
                new ("east0",1, MatterType.Light, RectSide.Right)
            });


            var leftIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
            var rightOut = parts[0, 0].GetPinAt(RectSide.Right).IDOutFlow;
            var rightIn = parts[0, 0].GetPinAt(RectSide.Right).IDInFlow;
            var leftOut = parts[0, 0].GetPinAt(RectSide.Left).IDOutFlow;

            var allPins = Component.GetAllPins(parts).SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
            var matrixRed = new SMatrix(allPins, new());
            // set the connections
            matrixRed.SetValues(new(){
                { (leftIn, rightOut), 1 },
                { (rightIn, leftOut), 1 },
            });
            var connections = new Dictionary<int, SMatrix>
            {
                { StandardWaveLengths.RedNM, matrixRed},
                { StandardWaveLengths.GreenNM, matrixRed},
                { StandardWaveLengths.BlueNM, matrixRed},
            };

            return new Component(connections, new(), "placeCell_StraightWG", "", parts, 0, "Straight", DiscreteRotation.R0);
        }

        public static Component CreateDirectionalCoupler()
        {
            int widthInTiles = 2;
            int heightInTiles = 2;

            Part[,] parts = new Part[widthInTiles, heightInTiles];


            parts[0, 0] = new Part(new List<Pin>() { new Pin("west0", 0, MatterType.Light, RectSide.Left) });
            parts[1, 0] = new Part(new List<Pin>() { new Pin("east0", 1, MatterType.Light, RectSide.Right) });
            parts[1, 1] = new Part(new List<Pin>() { new Pin("east1", 2, MatterType.Light, RectSide.Right) });
            parts[0, 1] = new Part(new List<Pin>() { new Pin("west1", 3, MatterType.Light, RectSide.Left) });

            // setting up the connections
            var leftUpIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
            var leftUpOut = parts[0, 0].GetPinAt(RectSide.Left).IDOutFlow;
            var leftDownIn = parts[0, 1].GetPinAt(RectSide.Left).IDInFlow;
            var leftDownOut = parts[0, 1].GetPinAt(RectSide.Left).IDOutFlow;
            var rightUpIn = parts[1, 0].GetPinAt(RectSide.Right).IDInFlow;
            var rightUpOut = parts[1, 0].GetPinAt(RectSide.Right).IDOutFlow;
            var rightDownIn = parts[1, 1].GetPinAt(RectSide.Right).IDInFlow;
            var rightDownOut = parts[1, 1].GetPinAt(RectSide.Right).IDOutFlow;

            var allPins = Component.GetAllPins(parts).SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
            var matrixRed = new SMatrix(allPins, new());
            // set the connections
            matrixRed.SetValues(new(){
                { (leftUpIn, rightUpOut), Math.Sqrt(0.5f) },
                { (leftUpIn, rightDownOut), Math.Sqrt(0.5f) },
                { (leftDownIn, rightUpOut), Math.Sqrt(0.5f) },
                { (leftDownIn, rightDownOut), Math.Sqrt(0.5f) },
                { (rightUpIn, leftUpOut), Math.Sqrt(0.5f) },
                { (rightUpIn, leftDownOut), Math.Sqrt(0.5f) },
                { (rightDownIn, leftUpOut), Math.Sqrt(0.5f) },
                { (rightDownIn, leftDownOut), Math.Sqrt(0.5f) },

            });
            var connections = new Dictionary<int, SMatrix>
            {
                {StandardWaveLengths.RedNM, matrixRed},
                {StandardWaveLengths.GreenNM, matrixRed},
                {StandardWaveLengths.BlueNM, matrixRed},
            };
            return new Component(connections, new(), "placeCell_DirectionalCoupler", "", parts, 0, "DirectionalCoupler", DiscreteRotation.R0);
        }

        /// <summary>
        /// Creates a basic component for testing (uses CreateStraightWaveGuide).
        /// </summary>
        public static Component CreateBasicComponent()
        {
            var component = CreateStraightWaveGuide();
            component.PhysicalX = 0;
            component.PhysicalY = 0;
            component.WidthMicrometers = 250;
            component.HeightMicrometers = 250;
            return component;
        }

        /// <summary>
        /// Creates a WaveguideConnection between two components for testing.
        /// </summary>
        public static WaveguideConnection CreateConnection(Component startComponent, Component endComponent)
        {
            // Ensure components have physical pins
            if (startComponent.PhysicalPins.Count == 0)
            {
                var startPin = new PhysicalPin
                {
                    Name = "out",
                    ParentComponent = startComponent,
                    OffsetXMicrometers = 250,
                    OffsetYMicrometers = 125,
                    AngleDegrees = 0
                };
                startComponent.PhysicalPins.Add(startPin);
            }

            if (endComponent.PhysicalPins.Count == 0)
            {
                var endPin = new PhysicalPin
                {
                    Name = "in",
                    ParentComponent = endComponent,
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 125,
                    AngleDegrees = 180
                };
                endComponent.PhysicalPins.Add(endPin);
            }

            var connection = new WaveguideConnection
            {
                StartPin = startComponent.PhysicalPins[0],
                EndPin = endComponent.PhysicalPins[0]
            };

            return connection;
        }
    }
}
