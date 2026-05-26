using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImagePromptStudio;

public sealed class ProjectInfo : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "Default";
    private string _slug = "default";
    private bool _isDefault;

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Untitled Project" : value);
    }

    [JsonPropertyName("slug")]
    public string Slug
    {
        get => _slug;
        set => SetField(ref _slug, string.IsNullOrWhiteSpace(value) ? "project" : value);
    }

    [JsonPropertyName("is_default")]
    public bool IsDefault
    {
        get => _isDefault;
        set => SetField(ref _isDefault, value);
    }

    [JsonIgnore]
    public string RootDirectory => AppPaths.GetProjectRoot(this);

    [JsonIgnore]
    public string HistoryPath => AppPaths.GetProjectHistoryPath(this);

    [JsonIgnore]
    public string GeneratedDirectory => AppPaths.GetProjectGeneratedDirectory(this);

    public override string ToString() => Name;
}

public sealed class ProjectRegistry
{
    [JsonPropertyName("active_project_id")]
    public string ActiveProjectId { get; set; } = "default";

    [JsonPropertyName("projects")]
    public List<ProjectInfo> Projects { get; set; } = [];
}

public static class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ProjectRegistry Load()
    {
        Directory.CreateDirectory(AppPaths.ProjectsDirectory);

        ProjectRegistry registry;
        var registryPath = File.Exists(AppPaths.ProjectRegistryPath)
            ? AppPaths.ProjectRegistryPath
            : AppPaths.LegacyProjectRegistryPath;

        if (File.Exists(registryPath))
        {
            try
            {
                registry = JsonSerializer.Deserialize<ProjectRegistry>(File.ReadAllText(registryPath), JsonOptions) ?? new ProjectRegistry();
            }
            catch
            {
                registry = new ProjectRegistry();
            }
        }
        else
        {
            registry = new ProjectRegistry();
        }

        EnsureDefaultProject(registry);
        foreach (var project in registry.Projects)
        {
            Directory.CreateDirectory(project.GeneratedDirectory);
            if (!File.Exists(project.HistoryPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(project.HistoryPath)!);
                File.WriteAllText(project.HistoryPath, "[]");
            }
        }

        if (registry.Projects.All(project => project.Id != registry.ActiveProjectId))
        {
            registry.ActiveProjectId = registry.Projects[0].Id;
        }

        Save(registry);
        return registry;
    }

    public static void Save(ProjectRegistry registry)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(registry, JsonOptions);
        File.WriteAllText(AppPaths.ProjectRegistryPath, json);
    }

    public static ProjectInfo CreateProject(string requestedName, IEnumerable<ProjectInfo> existingProjects)
    {
        var cleanName = string.IsNullOrWhiteSpace(requestedName) ? "Untitled Project" : requestedName.Trim();
        var existing = existingProjects.ToList();
        var baseSlug = Slugify(cleanName);
        var slug = baseSlug;
        var suffix = 2;
        while (existing.Any(project => project.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        {
            slug = $"{baseSlug}_{suffix}";
            suffix++;
        }

        var project = new ProjectInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = cleanName,
            Slug = slug,
            IsDefault = false,
        };
        Directory.CreateDirectory(project.GeneratedDirectory);
        File.WriteAllText(project.HistoryPath, "[]");
        return project;
    }

    private static void EnsureDefaultProject(ProjectRegistry registry)
    {
        var defaultProject = registry.Projects.FirstOrDefault(project => project.IsDefault);
        if (defaultProject == null)
        {
            defaultProject = new ProjectInfo
            {
                Id = "default",
                Name = "Default",
                Slug = "default",
                IsDefault = true,
            };
            registry.Projects.Insert(0, defaultProject);
        }
        else
        {
            defaultProject.Id = string.IsNullOrWhiteSpace(defaultProject.Id) ? "default" : defaultProject.Id;
            defaultProject.Name = string.IsNullOrWhiteSpace(defaultProject.Name) ? "Default" : defaultProject.Name;
            defaultProject.Slug = "default";
            defaultProject.IsDefault = true;
        }
    }

    private static string Slugify(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var slug = new string(chars).Trim('_');
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "project" : slug[..Math.Min(slug.Length, 42)];
    }
}
