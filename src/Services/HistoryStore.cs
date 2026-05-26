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
            foreach (var entry in entries)
            {
                Normalize(entry, activeJobIds);
                entry.ProjectId = project.Id;
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
