using CAP_Core.CodeExporter;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.Tiles;
using Shouldly;
using UnitTests.Grid;
using Xunit;

namespace UnitTests
{
    public class ExportNazcaTests
    {
        [Fact]
        public void NazcaCompilerTest()
        {
            var grid = new GridManager(24,12);
            var inputs = grid.ExternalPortManager.GetAllExternalInputs();
            int inputHeight = inputs.FirstOrDefault()?.TilePositionY ?? throw new Exception("there is no StandardInput defined");
            var firstComponent = TestComponentFactory.CreateDirectionalCoupler();
            grid.ComponentMover.PlaceComponent(0, inputHeight, firstComponent);
            var secondComponent = PlaceAndConcatenateComponent(grid, firstComponent);
            var thirdComponent = PlaceAndConcatenateComponent(grid, secondComponent);
            var fourthComponent = PlaceAndConcatenateComponent(grid, thirdComponent);
            var orphan = TestComponentFactory.CreateDirectionalCoupler();
            grid.ComponentMover.PlaceComponent(10, 5, orphan);
            
            NazcaExporter exporter = new();
            var output = exporter.Export(grid);
            var firstCellName = grid.TileManager.Tiles[firstComponent.GridXMainTile, firstComponent.GridYMainTile].GetComponentCellName();
            var secondCellName = grid.TileManager.Tiles[secondComponent.GridXMainTile, secondComponent.GridYMainTile].GetComponentCellName();
            var thirdCellName = grid.TileManager.Tiles[thirdComponent.GridXMainTile, thirdComponent.GridYMainTile].GetComponentCellName();
            var fourthCellName = grid.TileManager.Tiles[fourthComponent.GridXMainTile, fourthComponent.GridYMainTile].GetComponentCellName();
            var orphanCellName = grid.TileManager.Tiles[orphan.GridXMainTile, orphan.GridYMainTile].GetComponentCellName();
            
            // assert if all components are in the string output
            output.ShouldContain(firstCellName);
            output.ShouldContain(secondCellName);
            output.ShouldContain(thirdCellName);
            output.ShouldContain(fourthCellName);
            output.ShouldContain(orphanCellName);
        }
        
        [Fact]
        public void GetConnectedNeighborsTest()
        {
            var grid = new GridManager(24,12);
            var inputs = grid.ExternalPortManager.GetAllExternalInputs();
            var firstInput = inputs.FirstOrDefault();
            if (firstInput == null) throw new Exception("Inputs not found, they seem not to be declared in the grid. Please do that now");
            var inputHeight = firstInput.TilePositionY;
            var firstComponent = TestComponentFactory.CreateStraightWaveGuide();
            // add grid components and tiles
            grid.ComponentMover.PlaceComponent(0, inputHeight, firstComponent);
            var secondComponent = PlaceAndConcatenateComponent(grid, firstComponent);
            var thirdComponent = PlaceAndConcatenateComponent(grid, secondComponent);
            
            // test if parameters in NazcaFunctionParameters work - like the DirectionalCoupler
            Tile firstComponentMainTile = grid.TileManager.Tiles[firstComponent.GridXMainTile, inputHeight];
            Tile secondComponentMainTile = grid.TileManager.Tiles[secondComponent.GridXMainTile, secondComponent.GridYMainTile];
            Tile thirdComponentMainTile = grid.TileManager.Tiles[thirdComponent.GridXMainTile, thirdComponent.GridYMainTile];
            
            grid.ComponentRelationshipManager.GetConnectedNeighborsOfComponent(firstComponent).Select(b=>b.Child).ShouldContain(secondComponentMainTile);
            grid.ComponentRelationshipManager.GetConnectedNeighborsOfComponent(secondComponent).Select(b => b.Child).ShouldContain(thirdComponentMainTile);
            grid.ComponentRelationshipManager.GetConnectedNeighborsOfComponent(secondComponent).Select(b => b.Child).ShouldContain(firstComponentMainTile);
        }
        
        public static Component PlaceAndConcatenateComponent(GridManager grid, Component parentComponent)
        {
            Component newComponent = TestComponentFactory.CreateStraightWaveGuide();
            var GridXSecondComponent = parentComponent.GridXMainTile + parentComponent.WidthInTiles;
            var GridYSecondComponent = parentComponent.GridYMainTile + parentComponent.HeightInTiles-1;
            grid.ComponentMover.PlaceComponent(GridXSecondComponent, GridYSecondComponent, newComponent);
            return newComponent;
        }
    }
}
