using System.Text.Json;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Creation;

/// <summary>
/// Manages the library of saved ComponentGroup templates.
/// Handles loading, saving, and instantiation of group templates following the
/// Unity Prefab pattern (templates in library, instantiated as ungrouped components).
///
/// Storage locations:
/// - User templates: %LocalAppData%/ConnectAPICPro/GroupLibrary/UserGroups/
/// - PDK templates: %LocalAppData%/ConnectAPICPro/GroupLibrary/PdkGroups/
///
/// Template lifecycle:
/// 1. Creation: User selects components → "Save as Template" → SaveTemplate()
/// 2. Storage: JSON file with metadata (name, description, component count)
/// 3. Loading: LoadTemplates() reads all JSON files and creates GroupTemplate objects
/// 4. Instantiation: PlaceTemplateCommand uses template.DeepCopy() to place on canvas
///
/// See docs/ComponentGroup-Architecture.md for detailed design documentation.
/// </summary>
public class GroupLibraryManager
{
    private const string DefaultLibraryFolder = "GroupLibrary";
    private const string UserGroupsSubfolder = "UserGroups";
    private const string PdkGroupsSubfolder = "PdkGroups";

    private readonly string _libraryBasePath;
    private readonly List<GroupTemplate> _templates = new();

    /// <summary>
    /// All loaded group templates (user + PDK).
    /// </summary>
    public IReadOnlyList<GroupTemplate> Templates => _templates;

    /// <summary>
    /// User-created group templates.
    /// </summary>
    public IEnumerable<GroupTemplate> UserTemplates =>
        _templates.Where(t => t.Source == "User");

    /// <summary>
    /// PDK-provided group templates (macros).
    /// </summary>
    public IEnumerable<GroupTemplate> PdkTemplates =>
        _templates.Where(t => t.Source == "PDK");

    /// <summary>
    /// Initializes the group library manager.
    /// </summary>
    /// <param name="basePath">Base path for library storage. If null, uses application data folder.</param>
    public GroupLibraryManager(string? basePath = null)
    {
        _libraryBasePath = basePath ?? GetDefaultLibraryPath();
        EnsureLibraryDirectoriesExist();
    }

    /// <summary>
    /// Loads all group templates from the library folders.
    /// </summary>
    public void LoadTemplates()
    {
        _templates.Clear();

        // Load user groups
        LoadTemplatesFromFolder(
            Path.Combine(_libraryBasePath, UserGroupsSubfolder),
            "User");

        // Load PDK groups
        LoadTemplatesFromFolder(
            Path.Combine(_libraryBasePath, PdkGroupsSubfolder),
            "PDK");
    }

    /// <summary>
    /// Saves a ComponentGroup as a template to the library.
    /// Marks the group as a prefab (IsPrefab = true) and stores it as a JSON file.
    ///
    /// Typical usage:
    /// 1. User selects multiple components on canvas
    /// 2. Calls "Save Selection as Template"
    /// 3. ViewModel creates ComponentGroup from selection
    /// 4. Calls this method to persist template
    ///
    /// The template can later be instantiated via PlaceTemplateCommand, which
    /// ungroups the components and places them as top-level elements on canvas.
    /// </summary>
    /// <param name="group">The group to save (with ChildComponents and InternalPaths populated)</param>
    /// <param name="name">Display name for the template (shown in library panel)</param>
    /// <param name="description">Optional description (shown in tooltip/preview)</param>
    /// <param name="source">Source category ("User" for user-created, "PDK" for PDK macros)</param>
    /// <returns>The created GroupTemplate object (added to Templates list)</returns>
    public GroupTemplate SaveTemplate(
        ComponentGroup group,
        string name,
        string? description = null,
        string source = "User")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name cannot be empty.", nameof(name));

        // Mark group as prefab
        group.IsPrefab = true;

        // Determine target folder
        var targetFolder = source == "User"
            ? Path.Combine(_libraryBasePath, UserGroupsSubfolder)
            : Path.Combine(_libraryBasePath, PdkGroupsSubfolder);

        // Generate unique filename
        var safeName = MakeSafeFilename(name);
        var fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(targetFolder, fileName);

        // Create file data structure (minimal metadata only)
        // Actual serialization will be handled by the persistence layer
        var fileData = new GroupLibraryFileData
        {
            Name = name,
            Description = description ?? "",
            Source = source,
            CreatedAt = DateTime.Now,
            ComponentCount = group.ChildComponents.Count,
            WidthMicrometers = group.WidthMicrometers,
            HeightMicrometers = group.HeightMicrometers
        };

        var json = JsonSerializer.Serialize(fileData, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);

        // Create template
        var template = new GroupTemplate
        {
            Name = name,
            Description = description ?? "",
            Category = source == "User" ? "User Groups" : "PDK Macros",
            FilePath = filePath,
            ComponentCount = group.ChildComponents.Count,
            WidthMicrometers = group.WidthMicrometers,
            HeightMicrometers = group.HeightMicrometers,
            Source = source,
            TemplateGroup = group,
            CreatedAt = DateTime.Now
        };

        _templates.Add(template);
        return template;
    }

    /// <summary>
    /// Removes a template from the library and deletes its file.
    /// </summary>
    /// <param name="template">The template to remove.</param>
    /// <returns>True if successfully removed.</returns>
    public bool RemoveTemplate(GroupTemplate template)
    {
        if (!_templates.Contains(template))
            return false;

        // Delete file if it exists
        if (template.FilePath != null && File.Exists(template.FilePath))
        {
            try
            {
                File.Delete(template.FilePath);
            }
            catch
            {
                // Ignore file deletion errors
            }
        }

        return _templates.Remove(template);
    }

    /// <summary>
    /// Creates a deep copy of a group template for instantiation on the canvas.
    /// NOTE: PlaceTemplateCommand typically handles instantiation and ungrouping.
    /// This method is useful for programmatic template instantiation outside the command pattern.
    ///
    /// The returned ComponentGroup is NOT added to canvas directly. Use PlaceTemplateCommand
    /// to properly ungroup and place components as top-level elements.
    /// </summary>
    /// <param name="template">The template to instantiate (must have TemplateGroup loaded)</param>
    /// <param name="x">X position for the new instance origin (micrometers)</param>
    /// <param name="y">Y position for the new instance origin (micrometers)</param>
    /// <returns>A new ComponentGroup instance with unique GUIDs for all components/paths</returns>
    public ComponentGroup InstantiateTemplate(
        GroupTemplate template,
        double x,
        double y)
    {
        if (template.TemplateGroup == null)
            throw new InvalidOperationException("Template group is not loaded.");

        var deepCopy = template.TemplateGroup.DeepCopy();
        deepCopy.PhysicalX = x;
        deepCopy.PhysicalY = y;
        deepCopy.GroupName = $"{template.Name}_{DateTime.Now:HHmmss}";

        return deepCopy;
    }

    /// <summary>
    /// Gets the default library path in the application data folder.
    /// </summary>
    private static string GetDefaultLibraryPath()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "ConnectAPICPro", DefaultLibraryFolder);
    }

    /// <summary>
    /// Ensures library subdirectories exist.
    /// </summary>
    private void EnsureLibraryDirectoriesExist()
    {
        Directory.CreateDirectory(Path.Combine(_libraryBasePath, UserGroupsSubfolder));
        Directory.CreateDirectory(Path.Combine(_libraryBasePath, PdkGroupsSubfolder));
    }

    /// <summary>
    /// Loads templates from a specific folder.
    /// </summary>
    private void LoadTemplatesFromFolder(string folderPath, string source)
    {
        if (!Directory.Exists(folderPath))
            return;

        foreach (var filePath in Directory.GetFiles(folderPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var fileData = JsonSerializer.Deserialize<GroupLibraryFileData>(json);

                if (fileData == null)
                    continue;

                var template = new GroupTemplate
                {
                    Name = fileData.Name,
                    Description = fileData.Description,
                    Category = source == "User" ? "User Groups" : "PDK Macros",
                    FilePath = filePath,
                    ComponentCount = fileData.ComponentCount,
                    WidthMicrometers = fileData.WidthMicrometers,
                    HeightMicrometers = fileData.HeightMicrometers,
                    Source = source,
                    CreatedAt = fileData.CreatedAt,
                    PreviewThumbnailBase64 = fileData.PreviewThumbnailBase64
                    // Note: TemplateGroup is loaded on-demand when needed
                };

                _templates.Add(template);
            }
            catch
            {
                // Skip malformed files
            }
        }
    }

    /// <summary>
    /// Makes a filename-safe version of a string.
    /// </summary>
    private static string MakeSafeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// JSON file structure for saved group templates.
/// Stores metadata only - the actual ComponentGroup is stored in TemplateGroup property.
/// </summary>
public class GroupLibraryFileData
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "User";
    public DateTime CreatedAt { get; set; }
    public int ComponentCount { get; set; }
    public double WidthMicrometers { get; set; }
    public double HeightMicrometers { get; set; }
    public string? PreviewThumbnailBase64 { get; set; }
}
