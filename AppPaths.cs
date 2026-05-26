using System.IO;

namespace ImagePromptStudio;

public static class AppPaths
{
    public const string RootOverrideEnvironmentVariable = "IMAGE_PROMPT_STUDIO_ROOT";
    public const string RootMarkerFileName = ".image-prompt-studio-root";
    public const string ProjectRegistryFileName = "projects.json";
    public const string LegacyProjectRegistryFileName = "project.json";
    public const string HistoryFileName = "history.json";
    public const string GeneratedDirectoryName = "generated";
    public const string ProjectsDirectoryName = "projects";
    public const string PublishedAppDirectoryName = "app";
    public const string ProjectFileName = "ImagePromptStudio.csproj";
    public const string LauncherFileName = "launch.vbs";

    public static string RootDirectory { get; } = FindRootDirectory();

    public static string ProjectRegistryPath => Path.Combine(RootDirectory, ProjectRegistryFileName);
    public static string LegacyProjectRegistryPath => Path.Combine(RootDirectory, LegacyProjectRegistryFileName);
    public static string ProjectsDirectory => Path.Combine(RootDirectory, ProjectsDirectoryName);
    public static string DefaultHistoryPath => Path.Combine(RootDirectory, HistoryFileName);
    public static string DefaultGeneratedDirectory => Path.Combine(RootDirectory, GeneratedDirectoryName);

    public static string GetProjectRoot(ProjectInfo project)
    {
        return project.IsDefault
            ? RootDirectory
            : Path.Combine(ProjectsDirectory, CleanPathSegment(project.Slug));
    }

    public static string GetProjectHistoryPath(ProjectInfo project)
    {
        return Path.Combine(GetProjectRoot(project), HistoryFileName);
    }

    public static string GetProjectGeneratedDirectory(ProjectInfo project)
    {
        return Path.Combine(GetProjectRoot(project), GeneratedDirectoryName);
    }

    private static string FindRootDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return NormalizeDirectory(Environment.ExpandEnvironmentVariables(overridePath)).FullName;
        }

        var executableDirectory = NormalizeDirectory(AppContext.BaseDirectory);
        var currentDirectory = NormalizeDirectory(Directory.GetCurrentDirectory());

        if (LooksLikeWorkspaceRoot(currentDirectory.FullName))
        {
            return currentDirectory.FullName;
        }

        if (executableDirectory.Name.Equals(PublishedAppDirectoryName, StringComparison.OrdinalIgnoreCase)
            && executableDirectory.Parent != null
            && LooksLikeWorkspaceRoot(executableDirectory.Parent.FullName))
        {
            return executableDirectory.Parent.FullName;
        }

        return executableDirectory.FullName;
    }

    private static bool LooksLikeWorkspaceRoot(string directory)
    {
        return File.Exists(Path.Combine(directory, RootMarkerFileName))
            || File.Exists(Path.Combine(directory, ProjectRegistryFileName))
            || File.Exists(Path.Combine(directory, LegacyProjectRegistryFileName))
            || File.Exists(Path.Combine(directory, ProjectFileName))
            || File.Exists(Path.Combine(directory, LauncherFileName))
            || (File.Exists(Path.Combine(directory, HistoryFileName))
                && (Directory.Exists(Path.Combine(directory, GeneratedDirectoryName))
                    || Directory.Exists(Path.Combine(directory, ProjectsDirectoryName))));
    }

    private static DirectoryInfo NormalizeDirectory(string path)
    {
        return new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string CleanPathSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = segment
            .Trim()
            .Select(ch => invalid.Contains(ch)
                || ch == Path.DirectorySeparatorChar
                || ch == Path.AltDirectorySeparatorChar
                    ? '_'
                    : ch)
            .ToArray();

        var cleaned = new string(chars).Trim(' ', '.', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }
}
