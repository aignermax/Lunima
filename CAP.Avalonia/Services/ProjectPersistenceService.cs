using CAP_Contracts;
using CAP_Core.Components.Creation;
using CAP_Core.Grid;
using CAP_DataAccess.Persistence;

namespace CAP.Avalonia.Services;

/// <summary>
/// Service for saving and loading projects with ComponentGroups support.
/// Wraps GridPersistenceWithGroupsManager for use in the Avalonia UI.
/// </summary>
public class ProjectPersistenceService
{
    private readonly GridManager _gridManager;
    private readonly IDataAccessor _dataAccessor;
    private readonly IComponentFactory _componentFactory;

    public ProjectPersistenceService(
        GridManager gridManager,
        IDataAccessor dataAccessor,
        IComponentFactory componentFactory)
    {
        _gridManager = gridManager;
        _dataAccessor = dataAccessor;
        _componentFactory = componentFactory;
    }

    /// <summary>
    /// Saves the current project to the specified file path.
    /// </summary>
    /// <param name="filePath">Path to save the project file.</param>
    /// <returns>True if save succeeded.</returns>
    public async Task<bool> SaveProjectAsync(string filePath)
    {
        var persistence = new GridPersistenceWithGroupsManager(_gridManager, _dataAccessor);
        return await persistence.SaveAsync(filePath);
    }

    /// <summary>
    /// Loads a project from the specified file path.
    /// </summary>
    /// <param name="filePath">Path to the project file.</param>
    public async Task LoadProjectAsync(string filePath)
    {
        var persistence = new GridPersistenceWithGroupsManager(_gridManager, _dataAccessor);
        await persistence.LoadAsync(filePath, _componentFactory);
    }
}
