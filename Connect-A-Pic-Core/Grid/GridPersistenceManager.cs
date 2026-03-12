using CAP_Contracts;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Tiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CAP_Core.Grid
{
    public class GridPersistenceManager
    {
        private List<ComponentGroup> _componentGroups = new();

        public GridPersistenceManager(GridManager myGrid, IDataAccessor dataAccessor)
        {
            MyGrid = myGrid;
            DataAccessor = dataAccessor;
        }

        public GridManager MyGrid { get; }
        public IDataAccessor DataAccessor { get; }

        /// <summary>
        /// Gets or sets the component groups associated with this design.
        /// </summary>
        public IReadOnlyList<ComponentGroup> ComponentGroups => _componentGroups.AsReadOnly();

        /// <summary>
        /// Adds a component group to be saved with the design.
        /// </summary>
        public void AddComponentGroup(ComponentGroup group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            if (!_componentGroups.Any(g => g.Id == group.Id))
            {
                _componentGroups.Add(group);
            }
        }

        /// <summary>
        /// Removes a component group from the design.
        /// </summary>
        public void RemoveComponentGroup(Guid groupId)
        {
            _componentGroups.RemoveAll(g => g.Id == groupId);
        }

        /// <summary>
        /// Clears all component groups from the design.
        /// </summary>
        public void ClearComponentGroups()
        {
            _componentGroups.Clear();
        }

        public async Task<bool> SaveAsync(string path)
        {
            List<GridComponentData> gridData = new();
            for (int x = 0; x < MyGrid.TileManager.Tiles.GetLength(0); x++)
            {
                for (int y = 0; y < MyGrid.TileManager.Tiles.GetLength(1); y++)
                {
                    if (MyGrid.TileManager.Tiles[x, y]?.Component == null) continue;
                    var component = MyGrid.TileManager.Tiles[x, y].Component;
                    if (x != component.GridXMainTile || y != component.GridYMainTile) continue;

                    gridData.Add(new GridComponentData()
                    {
                        Identifier = component.Identifier,
                        Rotation = (int)component.Rotation90CounterClock,
                        Sliders = component.GetAllSliders(),
                        X = x,
                        Y = y,
                    }) ;
                }
            }

            // Create design file data structure with components and groups
            var designData = new DesignFileData
            {
                Components = gridData,
                ComponentGroups = _componentGroups
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(designData, options);
            return await DataAccessor.Write(path, json);
        }
        public async Task LoadAsync(string path, IComponentFactory componentFactory)
        {
            var json = DataAccessor.ReadAsText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            // Try to load as new format with component groups
            DesignFileData? designData = null;
            List<GridComponentData>? gridData = null;

            try
            {
                designData = JsonSerializer.Deserialize<DesignFileData>(json, options);
            }
            catch
            {
                // Fall back to old format (just component list)
                designData = null;
            }

            if (designData?.Components != null)
            {
                // New format
                gridData = designData.Components;
                _componentGroups = designData.ComponentGroups ?? new List<ComponentGroup>();
            }
            else
            {
                // Old format - just a component array
                gridData = JsonSerializer.Deserialize<List<GridComponentData>>(json);
                _componentGroups.Clear();
            }

            MyGrid.ComponentMover.DeleteAllComponents();

            if (gridData != null)
            {
                foreach (var data in gridData)
                {
                    if (string.IsNullOrEmpty(data.Identifier))
                        continue;

                    var component = componentFactory.CreateComponentByIdentifier(data.Identifier);
                    if (component == null)
                        continue;

                    component.Rotation90CounterClock = (DiscreteRotation)data.Rotation;
                    LoadSliders(data, component);
                    MyGrid.ComponentMover.PlaceComponent(data.X, data.Y, component);
                }
            }
        }

        /// <summary>
        /// inserts the slider with Value, Number, Min/Max values from GridComponentData into the actual Component 
        /// Or updates its Value if the slider is already defined by the ComponentDraft
        /// </summary>
        /// <param name="data"></param>
        /// <param name="component"></param>
        private static void LoadSliders(GridComponentData? data, Component component)
        {
            if (data?.Sliders != null)
            {
                foreach (var sliderToLoad in data.Sliders)
                {
                    var predefinedSlider = component.GetSlider(sliderToLoad.Number);
                    if(predefinedSlider == null)
                    {
                        component.AddSlider(sliderToLoad.Number, sliderToLoad);
                    }
                    else
                    {
                        predefinedSlider.Value = sliderToLoad.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Container for the complete design file data including components and groups.
        /// </summary>
        public class DesignFileData
        {
            public List<GridComponentData> Components { get; set; } = new();
            public List<ComponentGroup>? ComponentGroups { get; set; }
        }

        public class GridComponentData
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Rotation { get; set; }
            public string Identifier { get; set; }
            public List<Slider>? Sliders { get; set; }
        }

        public class GridSliderData
        {
            public GridSliderData(int nr , double value)
            {
                Nr = nr;
                Value = value;
            }
            public int Nr { get; set; }
            public double Value { get; set; }
        }
    }

}
