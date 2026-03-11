using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Grid
{
    public interface IComponentRelationshipManager
    {
        public List<ParentAndChildTile> GetConnectedNeighborsOfComponent(Component component);
    }
}
