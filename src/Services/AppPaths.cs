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

    // The first line of a valid marker file must match this signature. Prevents a stray
    // file by the same name in some ancestor (e.g. an old renamed checkout, a home folder)
    // from silently diverting a distributed run into someone else's repo.
    public const string RootMarkerSignature = "image-prompt-studio-root-marker-v1";

    // Maximum number of parent directories the marker walk will traverse before giving up.
    // Guards against pathological inputs and keeps startup cost bounded.
    private const int MarkerWalkMaxDepth = 32;

    // Dev / repo data folder: shared by bin\Debug, bin\Release, and the published exe.
    public const string TestDataDirectoryName = "test-data";

    // Production data folder name (next to the distributed exe).
    public const string DefaultDataDirectoryName = "data";

    // Fallback subfolder under %LOCALAPPDATA% when the exe sits in a read-only location
    // such as Program Files. Picked instead of the default LocalApplicationData root to
    // keep state grouped under a single product folder.
    public const string LocalAppDataDirectoryName = "ImagePromptStudio";

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
            return NormalizeDirectory(Environment.ExpandEnvironmentVariables(overridePath));
        }

        var executableDirectory = GetExecutableDirectory();

        // Dev run: any ancestor directory of the exe carrying a valid marker means
        // "this is the repo; use the shared test-data folder".
        var markerDirectory = FindMarkerAncestor(executableDirectory);
        if (markerDirectory != null)
        {
            return Path.Combine(markerDirectory, TestDataDirectoryName);
        }

        // Distributed run: prefer a single subfolder next to the exe. If the exe lives
        // in a read-only location (Program Files, a network share with no write access,
        // etc.), fall back to %LOCALAPPDATA%\ImagePromptStudio\ so the app still works.
        var portable = Path.Combine(executableDirectory, DefaultDataDirectoryName);
        if (TryEnsureWritable(portable))
        {
            return portable;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var roaming = Path.Combine(localAppData, LocalAppDataDirectoryName);
            if (TryEnsureWritable(roaming))
            {
                return roaming;
            }
        }

        // Last resort: return the portable path even if it's not writable. Higher-level
        // code will surface a clear error when it tries to persist.
        return portable;
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
                return NormalizeDirectory(directory);
            }
        }
        return NormalizeDirectory(AppContext.BaseDirectory);
    }

    private static string? FindMarkerAncestor(string startDirectory)
    {
        try
        {
            var dir = new DirectoryInfo(startDirectory);
            for (var depth = 0; dir != null && depth < MarkerWalkMaxDepth; dir = dir.Parent, depth++)
            {
                var markerPath = Path.Combine(dir.FullName, RootMarkerFileName);
                if (File.Exists(markerPath) && MarkerHasValidSignature(markerPath))
                {
                    return dir.FullName;
                }
            }
        }
        catch
        {
            // Inaccessible parents are not a fatal condition; fall through to caller's fallback.
        }

        return null;
    }

    private static bool MarkerHasValidSignature(string markerPath)
    {
        try
        {
            using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            return string.Equals(firstLine?.Trim(), RootMarkerSignature, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        // Preserve drive roots like "D:\". Trimming the trailing separator would yield "D:",
        // which Windows interprets as drive-relative rather than the drive root.
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.TrimEndingDirectorySeparator(path);
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
