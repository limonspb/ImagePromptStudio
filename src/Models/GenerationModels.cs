using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ImagePromptStudio;

public static class JobStatus
{
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Error = "error";
    public const string Canceled = "canceled";
}

public sealed class GenerationSettings
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-image-1.5";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = MainWindow.ModeGenerate;

    [JsonPropertyName("size")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("quality")]
    public string Quality { get; set; } = "medium";

    [JsonPropertyName("background")]
    public string Background { get; set; } = "transparent";

    [JsonPropertyName("output_format")]
    public string OutputFormat { get; set; } = "png";

    [JsonPropertyName("input_fidelity")]
    public string InputFidelity { get; set; } = "high";

    [JsonPropertyName("augment")]
    public string Augment { get; set; } = "On";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonIgnore]
    public string Summary => $"{Model} | {Size} | {Quality} | {Background} | {OutputFormat}";

    [JsonIgnore]
    public string DetailText =>
        $"Model: {Model}\n" +
        $"Mode: {Mode}\n" +
        $"Size: {Size}\n" +
        $"Quality: {Quality}\n" +
        $"Background: {Background}\n" +
        $"Output: {OutputFormat}\n" +
        $"Input fidelity: {InputFidelity}\n" +
        $"Prompt augment: {Augment}\n" +
        $"Reference: {(!string.IsNullOrWhiteSpace(Reference) ? Reference : "None")}";
}

public sealed class HistoryEntry : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _projectId = "";
    private string _time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private string _status = JobStatus.Done;
    private string _prompt = "";
    private string _constraints = "";
    private string _negative = "";
    private string _output = "";
    private GenerationSettings _settings = new();
    private string _log = "";
    private string? _finished;
    private string? _error;
    private bool _deleteChecked;
    private BitmapImage? _thumbnail;

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    [JsonPropertyName("project_id")]
    public string ProjectId
    {
        get => _projectId;
        set => SetField(ref _projectId, value ?? "");
    }

    [JsonPropertyName("time")]
    public string Time
    {
        get => _time;
        set
        {
            if (SetField(ref _time, value ?? ""))
            {
                NotifyComputed();
            }
        }
    }

    [JsonPropertyName("status")]
    public string Status
    {
        get => _status;
        set
        {
            var normalized = value is JobStatus.InProgress or JobStatus.Done or JobStatus.Error or JobStatus.Canceled
                ? value
                : JobStatus.Error;
            if (SetField(ref _status, normalized))
            {
                NotifyComputed();
            }
        }
    }

    [JsonPropertyName("prompt")]
    public string Prompt
    {
        get => _prompt;
        set
        {
            if (SetField(ref _prompt, value ?? ""))
            {
                NotifyComputed();
            }
        }
    }

    [JsonPropertyName("constraints")]
    public string Constraints
    {
        get => _constraints;
        set => SetField(ref _constraints, value ?? "");
    }

    [JsonPropertyName("negative")]
    public string Negative
    {
        get => _negative;
        set => SetField(ref _negative, value ?? "");
    }

    [JsonPropertyName("output")]
    public string Output
    {
        get => _output;
        set
        {
            if (SetField(ref _output, value ?? ""))
            {
                NotifyComputed();
            }
        }
    }

    [JsonPropertyName("settings")]
    public GenerationSettings Settings
    {
        get => _settings;
        set
        {
            if (SetField(ref _settings, value ?? new GenerationSettings()))
            {
                NotifyComputed();
            }
        }
    }

    [JsonPropertyName("log")]
    public string Log
    {
        get => _log;
        set => SetField(ref _log, value ?? "");
    }

    [JsonPropertyName("finished")]
    public string? Finished
    {
        get => _finished;
        set => SetField(ref _finished, value);
    }

    [JsonPropertyName("error")]
    public string? Error
    {
        get => _error;
        set => SetField(ref _error, value);
    }

    [JsonIgnore]
    public bool DeleteChecked
    {
        get => _deleteChecked;
        set => SetField(ref _deleteChecked, value);
    }

    [JsonIgnore]
    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (SetField(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    [JsonIgnore]
    public bool IsInProgress => Status == JobStatus.InProgress;

    [JsonIgnore]
    public bool IsDone => Status == JobStatus.Done;

    [JsonIgnore]
    public bool IsError => Status == JobStatus.Error;

    [JsonIgnore]
    public bool IsCanceled => Status == JobStatus.Canceled;

    [JsonIgnore]
    public bool IsMissingFile => IsDone && !HasGeneratedFile;

    [JsonIgnore]
    public bool HasThumbnail => Thumbnail != null;

    [JsonIgnore]
    public bool HasGeneratedFile => !string.IsNullOrWhiteSpace(Output) && File.Exists(Output);

    [JsonIgnore]
    public string OutputFileName => string.IsNullOrWhiteSpace(Output) ? "No output file" : Path.GetFileName(Output);

    [JsonIgnore]
    public string StatusText => Status switch
    {
        JobStatus.InProgress => "In Progress",
        JobStatus.Canceled => "Canceled",
        JobStatus.Error => "Failed",
        _ => HasGeneratedFile ? "Done" : "Missing File",
    };

    [JsonIgnore]
    public string PromptPreview
    {
        get
        {
            var singleLine = (Prompt ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(singleLine))
            {
                return Settings.Mode == MainWindow.ModeEdit ? "Image edit" : "(empty prompt)";
            }

            var words = singleLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length <= 8 ? singleLine : string.Join(" ", words.Take(8)) + "...";
        }
    }

    [JsonIgnore]
    public string MetaLinePrimary => $"{Time} | {ModeLabel}";

    [JsonIgnore]
    public string MetaLineSecondary => Settings.Summary;

    [JsonIgnore]
    private string ModeLabel => Settings.Mode == MainWindow.ModeEdit ? "Edit" : "Generate";

    public void AppendLog(string text)
    {
        Log += text;
    }

    public void RefreshThumbnail()
    {
        try
        {
            Thumbnail = IsDone && HasGeneratedFile ? ImageLoader.LoadBitmap(Output, 132) : null;
        }
        catch
        {
            Thumbnail = null;
        }

        NotifyComputed();
    }

    public void NotifyTimingChanged()
    {
        OnPropertyChanged(nameof(StatusText));
    }

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsCanceled));
        OnPropertyChanged(nameof(IsMissingFile));
        OnPropertyChanged(nameof(HasGeneratedFile));
        OnPropertyChanged(nameof(OutputFileName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PromptPreview));
        OnPropertyChanged(nameof(MetaLinePrimary));
        OnPropertyChanged(nameof(MetaLineSecondary));
    }
}

public static class ImageLoader
{
    public static BitmapImage LoadBitmap(string path, int? decodePixelWidth = null)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        if (decodePixelWidth is > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        }
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
