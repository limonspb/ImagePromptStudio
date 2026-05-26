using System.IO;

namespace ImagePromptStudio;

public static class AppPaths
{
    // Set this env var to point at a specific data folder. Highest precedence.
    public const string DataOverrideEnvironmentVariable = "IMAGE_PROMPT_STUDIO_DATA";

    // Legacy override (older versions). Treated the same as IMAGE_PROMPT_STUDIO_DATA.
    public const string RootOverrideEnvironmentVariable = "IMAGE_PROMPT_STUDIO_ROOT";

    // Marker file at the repository root used to detect dev runs.
    // When present (found by walking up from the exe), the app uses TestDataDirectoryName.
    // When absent (distributed exe), the app falls back to DefaultDataDirectoryName next to the exe.
    public const string RootMarkerFileName = ".image-prompt-studio-root";

    // Dev / repo data folder: shared by bin\Debug, bin\Release, and the published exe.
    public const string TestDataDirectoryName = "test-data";

    // Production data folder name (next to the distributed exe).
    public const string DefaultDataDirectoryName = "data";

    public const string ProjectRegistryFileName = "projects.json";
    public const string LegacyProjectRegistryFileName = "project.json";
    public const string HistoryFileName = "history.json";
    public const string GeneratedDirectoryName = "generated";
    public const string ProjectsDirectoryName = "projects";

    // The directory that contains everything the app persists.
    public static string DataDirectory { get; } = FindDataDirectory();

    public static string ProjectRegistryPath => Path.Combine(DataDirectory, ProjectRegistryFileName);
    public static string LegacyProjectRegistryPath => Path.Combine(DataDirectory, LegacyProjectRegistryFileName);
    public static string ProjectsDirectory => Path.Combine(DataDirectory, ProjectsDirectoryName);
    public static string DefaultHistoryPath => Path.Combine(DataDirectory, HistoryFileName);
    public static string DefaultGeneratedDirectory => Path.Combine(DataDirectory, GeneratedDirectoryName);

    public static string GetProjectRoot(ProjectInfo project)
    {
        return project.IsDefault
            ? DataDirectory
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

    private static string FindDataDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(DataOverrideEnvironmentVariable)
                           ?? Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return NormalizeDirectory(Environment.ExpandEnvironmentVariables(overridePath)).FullName;
        }

        var executableDirectory = NormalizeDirectory(GetExecutableDirectory());

        // Dev run: any ancestor directory of the exe carrying the marker means
        // "this is the repo; use the shared test-data folder".
        var markerDirectory = FindMarkerAncestor(executableDirectory);
        if (markerDirectory != null)
        {
            return Path.Combine(markerDirectory.FullName, TestDataDirectoryName);
        }

        // Distributed run: keep all state in a single subfolder next to the exe.
        return Path.Combine(executableDirectory.FullName, DefaultDataDirectoryName);
    }

    private static string GetExecutableDirectory()
    {
        // Environment.ProcessPath returns the path of the launcher exe, even for
        // single-file published apps (where AppContext.BaseDirectory points to
        // the bundle extraction folder under %TEMP%\.net\...).
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }
        return AppContext.BaseDirectory;
    }

    private static DirectoryInfo? FindMarkerAncestor(DirectoryInfo start)
    {
        for (var dir = start; dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, RootMarkerFileName)))
            {
                return dir;
            }
        }
        return null;
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
