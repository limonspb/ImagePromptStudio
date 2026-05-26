using System.IO;
using System.Text.Json;

namespace ImagePromptStudio;

public static class HistoryStore
{
    public const int MaxEntries = 500;

    public static string? LastLoadWarning { get; private set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static List<HistoryEntry> Load(ProjectInfo project, IReadOnlySet<string>? activeJobIds = null)
    {
        LastLoadWarning = null;
        Directory.CreateDirectory(project.GeneratedDirectory);
        if (!File.Exists(project.HistoryPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(project.HistoryPath);
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? [];
            var relocatedCount = 0;
            foreach (var entry in entries)
            {
                Normalize(entry, activeJobIds);
                entry.ProjectId = project.Id;
                if (RelocateOutputIfMoved(entry, project.GeneratedDirectory))
                {
                    relocatedCount++;
                }
            }

            if (relocatedCount > 0)
            {
                try
                {
                    Save(project, entries);
                }
                catch
                {
                    // Save failures during a heal pass are non-fatal; the relocation
                    // still applies to this in-memory list.
                }
            }

            return entries.Take(MaxEntries).ToList();
        }
        catch (Exception ex)
        {
            var backupPath = BackupUnreadableHistory(project.HistoryPath);
            LastLoadWarning = backupPath == null
                ? $"Could not load history for {project.Name}: {ex.Message}"
                : $"Could not load history for {project.Name}. A backup was saved at {backupPath}.";
            return [];
        }
    }

    public static void Save(ProjectInfo project, IEnumerable<HistoryEntry> entries)
    {
        Directory.CreateDirectory(project.GeneratedDirectory);
        var json = JsonSerializer.Serialize(entries.Take(MaxEntries).ToList(), JsonOptions);
        File.WriteAllText(project.HistoryPath, json);
    }

    public static void Normalize(HistoryEntry entry, IReadOnlySet<string>? activeJobIds = null)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(entry.Time))
        {
            entry.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (entry.Status is not (JobStatus.InProgress or JobStatus.Done or JobStatus.Error or JobStatus.Canceled))
        {
            entry.Status = !string.IsNullOrWhiteSpace(entry.Output) ? JobStatus.Done : JobStatus.Error;
        }

        if (entry.Status == JobStatus.InProgress && activeJobIds?.Contains(entry.Id) != true)
        {
            entry.Status = JobStatus.Error;
            entry.Error = "This job was still running when the app closed.";
            entry.Finished ??= DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        entry.Settings ??= new GenerationSettings();
        entry.RefreshThumbnail();
    }

    /// <summary>
    /// If <paramref name="entry"/>'s recorded output path no longer points at an existing file,
    /// look for the same filename in the project's current generated directory and rewrite the
    /// path. Returns true if the entry was relocated. This lets a project's data folder be moved
    /// (or rebuilt from a backup) without breaking history thumbnails.
    /// </summary>
    private static bool RelocateOutputIfMoved(HistoryEntry entry, string generatedDirectory)
    {
        if (string.IsNullOrWhiteSpace(entry.Output))
        {
            return false;
        }

        if (File.Exists(entry.Output))
        {
            return false;
        }

        string fileName;
        try
        {
            fileName = Path.GetFileName(entry.Output);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var candidate = Path.Combine(generatedDirectory, fileName);
        if (!File.Exists(candidate))
        {
            return false;
        }

        entry.Output = candidate;
        entry.RefreshThumbnail();
        return true;
    }

    private static string? BackupUnreadableHistory(string historyPath)
    {
        try
        {
            if (!File.Exists(historyPath))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(historyPath) ?? AppPaths.DataDirectory;
            var fileName = Path.GetFileNameWithoutExtension(historyPath);
            var extension = Path.GetExtension(historyPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(directory, $"{fileName}.corrupt_{timestamp}{extension}");
            File.Copy(historyPath, backupPath, overwrite: false);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }
}
