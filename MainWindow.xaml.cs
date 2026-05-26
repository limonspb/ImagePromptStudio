using Microsoft.Win32;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImagePromptStudio;

public sealed record RunningJob(ProjectInfo Project, HistoryEntry Entry, CancellationTokenSource CancellationTokenSource);

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public const string ModeGenerate = "Generate new image";
    public const string ModeEdit = "Edit reference image";

    private static readonly string[] FallbackImageModels = ["gpt-image-1.5", "gpt-image-1", "gpt-image-1-mini", "gpt-image-2"];
    private static readonly string[] LegacySizes = ["1024x1024", "1024x1536", "1536x1024", "auto"];
    private static readonly string[] Gpt2Sizes = ["auto", "1024x1024", "1024x1536", "1536x1024", "1536x1536", "2048x1024", "1024x2048", "2048x2048"];

    private readonly ImageGenerationService _generator = new();
    private readonly OpenAiBudgetService _budgetService = new();
    private readonly Dictionary<string, RunningJob> _runningJobs = [];
    private readonly HashSet<string> _deletedRunningJobIds = [];
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _budgetTimer = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private ProjectRegistry _projectRegistry = new();
    private bool _isSwitchingProject;
    private bool _isSelectingHistory;
    private bool _isBudgetRefreshRunning;
    private string? _referenceImagePath;
    private ProjectInfo? _selectedProject;
    private HistoryEntry? _selectedEntry;
    private BitmapImage? _previewImage;
    private string? _previewImageKey;
    private string _previewMessage = "View a history item or generate a new image.";
    private string _selectedLog = "";
    private string _apiStatus = "";
    private string _openAiBudgetText = "";
    private string _openAiBudgetToolTip = "";
    private bool _isOpenAiBudgetVisible;
    private string _footerStatus = "";
    private string _referenceLabel = "No file selected";
    private double _previewZoom = 1.0;
    private double _previewOffsetX;
    private double _previewOffsetY;
    private Point? _previewDragStart;
    private double _previewDragStartOffsetX;
    private double _previewDragStartOffsetY;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProjectInfo> Projects { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];

    public ProjectInfo? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (_selectedProject == value)
            {
                return;
            }

            _selectedProject = value;
            OnPropertyChanged(nameof(SelectedProject));
            OnPropertyChanged(nameof(HistorySubheading));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public HistoryEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (_selectedEntry == value)
            {
                return;
            }

            _selectedEntry = value;
            OnPropertyChanged(nameof(SelectedEntry));
            OnPropertyChanged(nameof(CanUseSelectedImage));
            OnPropertyChanged(nameof(CanApplySelectedEntry));
            OnPropertyChanged(nameof(IsSelectedInProgress));
        }
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        set
        {
            if (_previewImage == value)
            {
                return;
            }

            _previewImage = value;
            OnPropertyChanged(nameof(PreviewImage));
            OnPropertyChanged(nameof(HasPreviewImage));
            OnPropertyChanged(nameof(IsPreviewMessageVisible));
            OnPropertyChanged(nameof(CanUseSelectedImage));
        }
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        set
        {
            if (_previewMessage == value)
            {
                return;
            }

            _previewMessage = value;
            OnPropertyChanged(nameof(PreviewMessage));
            OnPropertyChanged(nameof(IsPreviewMessageVisible));
        }
    }

    public double PreviewZoom
    {
        get => _previewZoom;
        set
        {
            var zoom = Math.Clamp(value, 1.0, 8.0);
            if (Math.Abs(_previewZoom - zoom) < 0.001)
            {
                return;
            }

            _previewZoom = zoom;
            OnPropertyChanged(nameof(PreviewZoom));
        }
    }

    public double PreviewOffsetX
    {
        get => _previewOffsetX;
        set
        {
            if (Math.Abs(_previewOffsetX - value) < 0.1)
            {
                return;
            }

            _previewOffsetX = value;
            OnPropertyChanged(nameof(PreviewOffsetX));
        }
    }

    public double PreviewOffsetY
    {
        get => _previewOffsetY;
        set
        {
            if (Math.Abs(_previewOffsetY - value) < 0.1)
            {
                return;
            }

            _previewOffsetY = value;
            OnPropertyChanged(nameof(PreviewOffsetY));
        }
    }

    public string SelectedLog
    {
        get => _selectedLog;
        set
        {
            if (_selectedLog == value)
            {
                return;
            }

            _selectedLog = value;
            OnPropertyChanged(nameof(SelectedLog));
        }
    }

    public string ApiStatus
    {
        get => _apiStatus;
        set
        {
            if (_apiStatus == value)
            {
                return;
            }

            _apiStatus = value;
            OnPropertyChanged(nameof(ApiStatus));
        }
    }

    public string OpenAiBudgetText
    {
        get => _openAiBudgetText;
        set
        {
            if (_openAiBudgetText == value)
            {
                return;
            }

            _openAiBudgetText = value;
            OnPropertyChanged(nameof(OpenAiBudgetText));
        }
    }

    public string OpenAiBudgetToolTip
    {
        get => _openAiBudgetToolTip;
        set
        {
            if (_openAiBudgetToolTip == value)
            {
                return;
            }

            _openAiBudgetToolTip = value;
            OnPropertyChanged(nameof(OpenAiBudgetToolTip));
        }
    }

    public bool IsOpenAiBudgetVisible
    {
        get => _isOpenAiBudgetVisible;
        set
        {
            if (_isOpenAiBudgetVisible == value)
            {
                return;
            }

            _isOpenAiBudgetVisible = value;
            OnPropertyChanged(nameof(IsOpenAiBudgetVisible));
        }
    }

    public string FooterStatus
    {
        get => _footerStatus;
        set
        {
            if (_footerStatus == value)
            {
                return;
            }

            _footerStatus = value;
            OnPropertyChanged(nameof(FooterStatus));
        }
    }

    public string ReferenceLabel
    {
        get => _referenceLabel;
        set
        {
            if (_referenceLabel == value)
            {
                return;
            }

            _referenceLabel = value;
            OnPropertyChanged(nameof(ReferenceLabel));
        }
    }

    public bool CanUseSelectedImage => SelectedEntry?.IsDone == true && SelectedEntry.HasGeneratedFile;
    public bool CanApplySelectedEntry => SelectedEntry != null;
    public bool HasPreviewImage => PreviewImage != null;
    public bool IsSelectedInProgress => SelectedEntry?.IsInProgress == true;
    public bool IsPreviewMessageVisible => PreviewImage == null || !string.IsNullOrWhiteSpace(PreviewMessage);
    public string HistorySubheading => SelectedProject == null
        ? "Completed items show previews. Active jobs show progress."
        : $"{SelectedProject.Name}: {History.Count} item(s), {History.Count(entry => entry.IsInProgress)} running.";

    public string WindowTitle
    {
        get
        {
            var running = _runningJobs.Count;
            var project = SelectedProject?.Name;
            var suffix = running > 0 ? $" — {running} running" : "";
            return string.IsNullOrWhiteSpace(project)
                ? "Image Prompt Studio" + suffix
                : $"Image Prompt Studio — {project}" + suffix;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        if (!_generator.HasApiKey)
        {
            var dialog = new OpenAiApiKeyDialog(this);
            if (dialog.ShowDialog() != true)
            {
                Dispatcher.BeginInvoke(Close);
                return;
            }

            try
            {
                OpenAiEnvironment.SetUserApiKey(dialog.ApiKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not save OPENAI_API_KEY:\n{ex.Message}", "OpenAI API key", MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.BeginInvoke(Close);
                return;
            }
        }

        ConfigureControls();
        _ = LoadAvailableModelsAsync();
        LoadProjects();
        LoadHistoryForSelectedProject();
        FooterStatus = HistoryStore.LastLoadWarning
            ?? (SelectedProject == null ? "No project selected." : $"Project: {SelectedProject.Name} | History: {SelectedProject.HistoryPath}");

        _timer.Interval = TimeSpan.FromMilliseconds(350);
        _timer.Tick += (_, _) =>
        {
            foreach (var entry in History.Where(entry => entry.IsInProgress))
            {
                entry.NotifyTimingChanged();
            }
        };
        _timer.Start();

        _budgetTimer.Interval = TimeSpan.FromSeconds(30);
        _budgetTimer.Tick += async (_, _) => await RefreshOpenAiBudgetAsync();
        _budgetTimer.Start();
        _ = RefreshOpenAiBudgetAsync();

        if (History.FirstOrDefault() is { } first)
        {
            ViewEntry(first);
        }
    }

    private void ConfigureControls()
    {
        ModelCombo.ItemsSource = FallbackImageModels;
        ModeCombo.ItemsSource = new[] { ModeGenerate, ModeEdit };
        SizeCombo.ItemsSource = LegacySizes;
        QualityCombo.ItemsSource = new[] { "medium", "high", "low", "auto" };
        BackgroundCombo.ItemsSource = new[] { "transparent", "opaque", "auto" };
        OutputCombo.ItemsSource = new[] { "png", "webp", "jpeg" };
        FidelityCombo.ItemsSource = new[] { "high", "low" };
        AugmentCombo.ItemsSource = new[] { "On", "Off" };

        ModelCombo.SelectedItem = "gpt-image-1.5";
        ModeCombo.SelectedItem = ModeGenerate;
        SizeCombo.SelectedItem = "1024x1024";
        QualityCombo.SelectedItem = "medium";
        BackgroundCombo.SelectedItem = "transparent";
        OutputCombo.SelectedItem = "png";
        FidelityCombo.SelectedItem = "high";
        AugmentCombo.SelectedItem = "On";

        PreviewBackgroundCombo.ItemsSource = new[] { "Checkerboard", "Black", "White", "Gray", "Green Screen", "Pink" };
        PreviewBackgroundCombo.SelectedItem = "Checkerboard";
        ProjectCombo.ItemsSource = Projects;

        AutoGrowTextBox(PromptBox);
        AutoGrowTextBox(ConstraintsBox);
        AutoGrowTextBox(NegativeBox);
        ApplyPreviewBackground();
        ApplyModelRules();
        ApplyModeRules();
    }

    private async Task LoadAvailableModelsAsync()
    {
        try
        {
            var currentModel = Selected(ModelCombo, "gpt-image-1.5");
            var models = await _generator.GetAvailableImageModelsAsync();
            if (models.Count == 0)
            {
                return;
            }

            ModelCombo.ItemsSource = models;
            ModelCombo.SelectedItem = models.Contains(currentModel, StringComparer.OrdinalIgnoreCase)
                ? currentModel
                : models.FirstOrDefault(model => model.Equals("gpt-image-1.5", StringComparison.OrdinalIgnoreCase)) ?? models[0];
            ApplyModelRules();
        }
        catch
        {
            ModelCombo.ItemsSource = FallbackImageModels;
            ModelCombo.SelectedItem ??= "gpt-image-1.5";
            ApplyModelRules();
        }
    }

    private void LoadProjects()
    {
        _projectRegistry = ProjectStore.Load();
        Projects.Clear();
        foreach (var project in _projectRegistry.Projects)
        {
            Projects.Add(project);
        }

        SelectedProject = Projects.FirstOrDefault(project => project.Id == _projectRegistry.ActiveProjectId) ?? Projects.FirstOrDefault();
        _isSwitchingProject = true;
        ProjectCombo.SelectedItem = SelectedProject;
        _isSwitchingProject = false;
    }

    private void LoadHistoryForSelectedProject()
    {
        History.Clear();
        if (SelectedProject == null)
        {
            RefreshProjectUi();
            return;
        }

        var activeJobIds = _runningJobs.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in HistoryStore.Load(SelectedProject, activeJobIds))
        {
            if (_runningJobs.TryGetValue(entry.Id, out var runningJob) && runningJob.Project.Id == SelectedProject.Id)
            {
                History.Add(runningJob.Entry);
            }
            else
            {
                History.Add(entry);
            }
        }

        RefreshProjectUi();
    }

    private void SaveHistory()
    {
        if (SelectedProject != null)
        {
            SaveHistory(SelectedProject, History);
        }
    }

    private void SaveHistory(ProjectInfo project, IEnumerable<HistoryEntry> entries)
    {
        HistoryStore.Save(project, entries);
        RefreshProjectUi();
    }

    private void SaveProjectEntry(ProjectInfo project, HistoryEntry updatedEntry)
    {
        if (_deletedRunningJobIds.Contains(updatedEntry.Id))
        {
            return;
        }

        updatedEntry.ProjectId = project.Id;

        if (SelectedProject?.Id == project.Id)
        {
            var existing = History.FirstOrDefault(entry => entry.Id == updatedEntry.Id);
            if (existing == null)
            {
                History.Insert(0, updatedEntry);
            }
            else if (!ReferenceEquals(existing, updatedEntry))
            {
                History[History.IndexOf(existing)] = updatedEntry;
            }

            SaveHistory(project, History);
            return;
        }

        var activeJobIds = _runningJobs.Keys.ToHashSet(StringComparer.Ordinal);
        var entries = HistoryStore.Load(project, activeJobIds);
        var index = entries.FindIndex(entry => entry.Id == updatedEntry.Id);
        if (index >= 0)
        {
            entries[index] = updatedEntry;
        }
        else
        {
            entries.Insert(0, updatedEntry);
        }

        HistoryStore.Save(project, entries);
        RefreshProjectUi();
    }

    private void RefreshProjectUi()
    {
        OnPropertyChanged(nameof(HistorySubheading));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void SaveProjects()
    {
        if (SelectedProject != null)
        {
            _projectRegistry.ActiveProjectId = SelectedProject.Id;
        }

        _projectRegistry.Projects = Projects.ToList();
        ProjectStore.Save(_projectRegistry);
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var prompt = PromptBox.Text.Trim();
        var constraints = ConstraintsBox.Text.Trim();
        var negative = NegativeBox.Text.Trim();
        var settings = CaptureSettings();

        if (string.IsNullOrWhiteSpace(prompt) && !CanRunEmptyPromptEdit(settings))
        {
            MessageBox.Show(this, "Type a prompt before generating, or switch to Edit reference image with a reference selected.", "Missing prompt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_generator.ValidateForRun(settings) is { } validationError)
        {
            MessageBox.Show(this, validationError, "Cannot generate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var project = SelectedProject;
        if (project == null)
        {
            MessageBox.Show(this, "Choose or create a project before generating.", "No project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entryId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(project.GeneratedDirectory);
        var outputPath = NextOutputPath(project, prompt, settings.OutputFormat, entryId);
        var logPreview = _generator.BuildLogPreview(prompt, constraints, negative, outputPath, settings);
        var entry = new HistoryEntry
        {
            Id = entryId,
            ProjectId = project.Id,
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Status = JobStatus.InProgress,
            Prompt = prompt,
            Constraints = constraints,
            Negative = negative,
            Output = outputPath,
            Settings = settings,
            Log = "Running:\n" + logPreview + "\n\n",
        };

        History.Insert(0, entry);
        while (History.Count > HistoryStore.MaxEntries)
        {
            History.RemoveAt(History.Count - 1);
        }

        SaveHistory();
        ViewEntry(entry);
        FooterStatus = $"Generating: {entry.OutputFileName}";
        _ = RunGenerationAsync(project, entry, prompt, constraints, negative, outputPath);
    }

    private static bool CanRunEmptyPromptEdit(GenerationSettings settings)
    {
        return settings.Mode == ModeEdit
            && !string.IsNullOrWhiteSpace(settings.Reference)
            && ImageGenerationService.SupportsImageEdit(settings.Model);
    }

    private async Task RunGenerationAsync(ProjectInfo project, HistoryEntry entry, string prompt, string constraints, string negative, string outputPath)
    {
        var cts = new CancellationTokenSource();
        _runningJobs[entry.Id] = new RunningJob(project, entry, cts);
        RefreshProjectUi();

        try
        {
            var result = await _generator.RunAsync(prompt, constraints, negative, outputPath, entry.Settings, cts.Token, line =>
            {
                Dispatcher.Invoke(() =>
                {
                    entry.AppendLog(line);
                    if (SelectedEntry?.Id == entry.Id)
                    {
                        SelectedLog = entry.Log;
                    }
                });
            });

            await Dispatcher.InvokeAsync(() =>
            {
                entry.Finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (_deletedRunningJobIds.Contains(entry.Id))
                {
                    return;
                }

                if (result.Success)
                {
                    entry.Status = JobStatus.Done;
                    entry.Output = outputPath;
                    entry.Error = null;
                    entry.AppendLog($"\nDone: {outputPath}\n");
                    entry.RefreshThumbnail();
                    FooterStatus = $"Generated: {entry.OutputFileName}";
                }
                else if (result.WasCanceled || cts.IsCancellationRequested)
                {
                    entry.Status = JobStatus.Canceled;
                    entry.Error = "Generation canceled.";
                    entry.AppendLog("\nGeneration canceled.\n");
                    FooterStatus = $"Canceled: {entry.OutputFileName}";
                }
                else
                {
                    entry.Status = JobStatus.Error;
                    entry.Error = result.Error ?? "Generation failed.";
                    entry.AppendLog("\n" + entry.Error + "\n");
                    FooterStatus = "Generation failed.";
                }

                if (SelectedEntry?.Id == entry.Id)
                {
                    ViewEntry(entry);
                }

                SaveProjectEntry(project, entry);
            });
        }
        finally
        {
            _runningJobs.Remove(entry.Id);
            _deletedRunningJobIds.Remove(entry.Id);
            RefreshProjectUi();
            _ = RefreshOpenAiBudgetAsync();
        }
    }

    private async Task RefreshOpenAiBudgetAsync()
    {
        if (_isBudgetRefreshRunning || _shutdownCts.IsCancellationRequested)
        {
            return;
        }

        _isBudgetRefreshRunning = true;
        try
        {
            var snapshot = await _budgetService.GetMonthToDateAsync(_shutdownCts.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (snapshot.IsAvailable)
                {
                    OpenAiBudgetText = snapshot.Text;
                    OpenAiBudgetToolTip = snapshot.Detail;
                    IsOpenAiBudgetVisible = true;
                }
                else
                {
                    OpenAiBudgetText = "";
                    OpenAiBudgetToolTip = "";
                    IsOpenAiBudgetVisible = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                OpenAiBudgetText = "";
                OpenAiBudgetToolTip = "";
                IsOpenAiBudgetVisible = false;
            });
        }
        finally
        {
            _isBudgetRefreshRunning = false;
        }
    }

    private GenerationSettings CaptureSettings()
    {
        return new GenerationSettings
        {
            Model = Selected(ModelCombo, "gpt-image-1.5"),
            Mode = Selected(ModeCombo, ModeGenerate),
            Size = Selected(SizeCombo, "1024x1024"),
            Quality = Selected(QualityCombo, "medium"),
            Background = Selected(BackgroundCombo, "transparent"),
            OutputFormat = Selected(OutputCombo, "png"),
            InputFidelity = Selected(FidelityCombo, "high"),
            Augment = Selected(AugmentCombo, "On"),
            Reference = _referenceImagePath,
        };
    }

    private static string Selected(System.Windows.Controls.ComboBox combo, string fallback)
    {
        return combo.SelectedItem as string ?? combo.Text ?? fallback;
    }

    private void AutoGrowTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            AutoGrowTextBox(textBox);
        }
    }

    private static void AutoGrowTextBox(TextBox textBox)
    {
        var lineCount = Math.Max(1, textBox.LineCount);
        var lineHeight = textBox.FontSize + 13;
        var desiredHeight = Math.Min(textBox.MaxHeight, Math.Max(textBox.MinHeight, lineCount * lineHeight));
        textBox.Height = desiredHeight;
        textBox.VerticalScrollBarVisibility = desiredHeight >= textBox.MaxHeight
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    private static string NextOutputPath(ProjectInfo project, string prompt, string extension, string entryId)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? "png" : extension.TrimStart('.');
        var suffixSource = string.IsNullOrWhiteSpace(entryId) ? Guid.NewGuid().ToString("N") : entryId;
        var suffix = suffixSource[..Math.Min(suffixSource.Length, 8)];
        return Path.Combine(project.GeneratedDirectory, $"{timestamp}_{ImageGenerationService.PromptSlug(prompt)}_{suffix}.{safeExtension}");
    }

    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HistoryEntry entry)
        {
            ViewEntry(entry, resetSameImage: true);
        }
    }

    private void ViewEntry(HistoryEntry entry, bool resetSameImage = false)
    {
        SelectedEntry = entry;
        if (HistoryList.SelectedItem != entry)
        {
            _isSelectingHistory = true;
            HistoryList.SelectedItem = entry;
            _isSelectingHistory = false;
        }

        SelectedLog = entry.Log;

        if (entry.IsInProgress)
        {
            ClearPreviewImage();
            PreviewMessage = "Generation in progress.";
        }
        else if (entry.IsError)
        {
            ClearPreviewImage();
            PreviewMessage = entry.Error ?? "Generation failed.";
        }
        else if (entry.IsCanceled)
        {
            ClearPreviewImage();
            PreviewMessage = entry.Error ?? "Generation canceled.";
        }
        else if (entry.HasGeneratedFile)
        {
            try
            {
                ShowPreviewImage(entry.Output, ImageLoader.LoadBitmap(entry.Output), resetSameImage);
                PreviewMessage = "";
                entry.RefreshThumbnail();
            }
            catch (Exception ex)
            {
                ClearPreviewImage();
                PreviewMessage = "Preview failed: " + ex.Message;
            }
        }
        else
        {
            ClearPreviewImage();
            PreviewMessage = "Generated file is missing.";
        }

        OnPropertyChanged(nameof(CanUseSelectedImage));
        OnPropertyChanged(nameof(IsSelectedInProgress));
        OnPropertyChanged(nameof(IsPreviewMessageVisible));
    }

    private void ShowPreviewImage(string imagePath, BitmapImage image, bool forceReset = false)
    {
        if (forceReset || !string.Equals(_previewImageKey, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            ResetPreviewTransform();
        }

        _previewImageKey = imagePath;
        PreviewImage = image;
    }

    private void ClearPreviewImage()
    {
        _previewImageKey = null;
        PreviewImage = null;
        ResetPreviewTransform();
    }

    private void ResetPreviewTransform()
    {
        EndPreviewDrag();
        PreviewZoom = 1.0;
        PreviewOffsetX = 0;
        PreviewOffsetY = 0;
    }

    private void ResetPreview_Click(object sender, RoutedEventArgs e)
    {
        ResetPreviewTransform();
    }

    private void PreviewBackgroundCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyPreviewBackground();
        }
    }

    private void ApplyPreviewBackground()
    {
        if (PreviewViewport == null)
        {
            return;
        }

        PreviewViewport.Background = (PreviewBackgroundCombo.SelectedItem as string) switch
        {
            "Black" => Brushes.Black,
            "White" => Brushes.White,
            "Gray" => Brushes.Gray,
            "Green Screen" => new SolidColorBrush(Color.FromRgb(0, 255, 0)),
            "Pink" => new SolidColorBrush(Color.FromRgb(255, 0, 255)),
            _ => (Brush)FindResource("CheckerBrush"),
        };
    }

    private void PreviewViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PreviewImage == null)
        {
            return;
        }

        var oldZoom = PreviewZoom;
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newZoom = Math.Clamp(oldZoom * factor, 1.0, 8.0);
        if (Math.Abs(newZoom - oldZoom) < 0.001)
        {
            return;
        }

        if (newZoom <= 1.001)
        {
            ResetPreviewTransform();
            e.Handled = true;
            return;
        }

        var position = e.GetPosition(PreviewViewport);
        var center = new Point(PreviewViewport.ActualWidth / 2, PreviewViewport.ActualHeight / 2);
        var relativeX = position.X - center.X - PreviewOffsetX;
        var relativeY = position.Y - center.Y - PreviewOffsetY;
        var ratio = newZoom / oldZoom;

        PreviewZoom = newZoom;
        PreviewOffsetX -= relativeX * (ratio - 1);
        PreviewOffsetY -= relativeY * (ratio - 1);
        ClampPreviewOffsets();
        e.Handled = true;
    }

    private void PreviewViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PreviewImage == null)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            ResetPreviewTransform();
            e.Handled = true;
            return;
        }

        if (PreviewZoom <= 1.001)
        {
            return;
        }

        _previewDragStart = e.GetPosition(PreviewViewport);
        _previewDragStartOffsetX = PreviewOffsetX;
        _previewDragStartOffsetY = PreviewOffsetY;
        PreviewViewport.CaptureMouse();
        PreviewViewport.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void PreviewViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_previewDragStart == null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndPreviewDrag();
            return;
        }

        var position = e.GetPosition(PreviewViewport);
        PreviewOffsetX = _previewDragStartOffsetX + position.X - _previewDragStart.Value.X;
        PreviewOffsetY = _previewDragStartOffsetY + position.Y - _previewDragStart.Value.Y;
        ClampPreviewOffsets();
        e.Handled = true;
    }

    private void PreviewViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPreviewDrag();
        e.Handled = true;
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClampPreviewOffsets();
    }

    private void EndPreviewDrag()
    {
        _previewDragStart = null;
        if (PreviewViewport?.IsMouseCaptured == true)
        {
            PreviewViewport.ReleaseMouseCapture();
        }

        if (PreviewViewport != null)
        {
            PreviewViewport.Cursor = null;
        }
    }

    private void ClampPreviewOffsets()
    {
        if (PreviewZoom <= 1.001 || PreviewViewport.ActualWidth <= 0 || PreviewViewport.ActualHeight <= 0)
        {
            PreviewOffsetX = 0;
            PreviewOffsetY = 0;
            return;
        }

        var contentSize = GetPreviewContentSize();
        var viewportWidth = Math.Max(0, PreviewViewport.ActualWidth - PreviewImageControl.Margin.Left - PreviewImageControl.Margin.Right);
        var viewportHeight = Math.Max(0, PreviewViewport.ActualHeight - PreviewImageControl.Margin.Top - PreviewImageControl.Margin.Bottom);
        var maxX = Math.Max(0, (contentSize.Width * PreviewZoom - viewportWidth) / 2);
        var maxY = Math.Max(0, (contentSize.Height * PreviewZoom - viewportHeight) / 2);
        PreviewOffsetX = Math.Clamp(PreviewOffsetX, -maxX, maxX);
        PreviewOffsetY = Math.Clamp(PreviewOffsetY, -maxY, maxY);
    }

    private Size GetPreviewContentSize()
    {
        if (PreviewImage == null)
        {
            return new Size(0, 0);
        }

        var viewportWidth = Math.Max(0, PreviewViewport.ActualWidth - PreviewImageControl.Margin.Left - PreviewImageControl.Margin.Right);
        var viewportHeight = Math.Max(0, PreviewViewport.ActualHeight - PreviewImageControl.Margin.Top - PreviewImageControl.Margin.Bottom);
        if (viewportWidth <= 0 || viewportHeight <= 0 || PreviewImage.PixelWidth <= 0 || PreviewImage.PixelHeight <= 0)
        {
            return new Size(0, 0);
        }

        var scale = Math.Min(viewportWidth / PreviewImage.PixelWidth, viewportHeight / PreviewImage.PixelHeight);
        return new Size(PreviewImage.PixelWidth * scale, PreviewImage.PixelHeight * scale);
    }

    private void HistoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isSelectingHistory)
        {
            return;
        }

        if (HistoryList.SelectedItem is HistoryEntry entry)
        {
            ViewEntry(entry);
        }
    }

    private void HistoryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item && !item.IsSelected)
        {
            HistoryList.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void DeleteSelectedHistory_Click(object sender, RoutedEventArgs e)
    {
        var entries = HistoryList.SelectedItems.Cast<HistoryEntry>().ToList();
        if (entries.Count == 0)
        {
            FooterStatus = "No selected history items.";
            return;
        }

        var projectName = SelectedProject?.Name ?? "current project";
        if (MessageBox.Show(this, $"Delete {entries.Count} selected history item(s) from {projectName}? Generated image files will be kept.", "Delete selected", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteEntries(entries);
        FooterStatus = $"Deleted {entries.Count} history item(s).";
    }

    private void DeleteAllHistory_Click(object sender, RoutedEventArgs e)
    {
        if (History.Count == 0)
        {
            FooterStatus = "History is already empty.";
            return;
        }

        var projectName = SelectedProject?.Name ?? "current project";
        if (MessageBox.Show(this, $"Delete all {History.Count} history item(s) from {projectName}? Generated image files will be kept.", "Delete all history", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var count = History.Count;
        DeleteEntries(History.ToList());
        FooterStatus = $"Deleted all {count} history item(s).";
    }

    private void DeleteEntries(IEnumerable<HistoryEntry> entries)
    {
        var entryList = entries.ToList();
        foreach (var entry in entryList)
        {
            if (_runningJobs.TryGetValue(entry.Id, out var runningJob))
            {
                _deletedRunningJobIds.Add(entry.Id);
                runningJob.CancellationTokenSource.Cancel();
            }
        }

        var clearSelectedPreview = SelectedEntry != null && entryList.Any(entry => entry.Id == SelectedEntry.Id);
        HistoryList.SelectedItems.Clear();
        foreach (var entry in entryList)
        {
            History.Remove(entry);
        }

        if (clearSelectedPreview)
        {
            SelectedEntry = null;
            ClearPreviewImage();
            PreviewMessage = "View a history item or generate a new image.";
            SelectedLog = "";
        }

        SaveHistory();
    }

    private void CancelSelectedHistory_Click(object sender, RoutedEventArgs e)
    {
        var entries = HistoryList.SelectedItems.Cast<HistoryEntry>().Where(entry => entry.IsInProgress).ToList();
        if (entries.Count == 0)
        {
            FooterStatus = "No selected running items.";
            return;
        }

        foreach (var entry in entries)
        {
            CancelEntry(entry);
        }

        FooterStatus = $"Canceling {entries.Count} selected item(s).";
    }

    private void CancelEntry(HistoryEntry entry)
    {
        if (!_runningJobs.TryGetValue(entry.Id, out var runningJob))
        {
            return;
        }

        entry.AppendLog("\nCancel requested.\n");
        entry.Status = JobStatus.Canceled;
        entry.Error = "Generation canceled.";
        entry.Finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        runningJob.CancellationTokenSource.Cancel();
        if (SelectedEntry?.Id == entry.Id)
        {
            ViewEntry(entry);
        }

        SaveProjectEntry(runningJob.Project, entry);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry?.HasGeneratedFile != true)
        {
            return;
        }

        var ext = Path.GetExtension(SelectedEntry.Output);
        var dialog = new SaveFileDialog
        {
            Title = "Save generated image",
            FileName = Path.GetFileName(SelectedEntry.Output),
            DefaultExt = ext,
            Filter = "PNG|*.png|WebP|*.webp|JPEG|*.jpg;*.jpeg|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.Copy(SelectedEntry.Output, dialog.FileName, overwrite: true);
            FooterStatus = $"Saved: {dialog.FileName}";
        }
    }

    private void PreviewContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var canUseImage = CanUseSelectedImage;
        PreviewCopyFileMenuItem.IsEnabled = canUseImage;
        PreviewCopyImageMenuItem.IsEnabled = canUseImage;
        PreviewSaveAsMenuItem.IsEnabled = canUseImage;
        PreviewShowInExplorerMenuItem.IsEnabled = canUseImage;
    }

    private void CopyAsFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry?.HasGeneratedFile != true)
        {
            return;
        }

        var files = new StringCollection
        {
            SelectedEntry.Output,
        };
        Clipboard.SetFileDropList(files);
        FooterStatus = "Copied image file to clipboard.";
    }

    private void CopyAsImage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry?.HasGeneratedFile != true)
        {
            return;
        }

        try
        {
            CopyImageToClipboard(SelectedEntry.Output);
            FooterStatus = "Copied image pixels to clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void CopyImageToClipboard(string imagePath)
    {
        var bitmap = ImageLoader.LoadBitmap(imagePath);
        var pngBytes = EncodePng(bitmap);
        var data = new DataObject();
        data.SetImage(bitmap);
        data.SetData("PNG", new MemoryStream(pngBytes));
        data.SetData("image/png", new MemoryStream(pngBytes));
        Clipboard.SetDataObject(data, copy: true);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private void ApplyViewed_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry == null)
        {
            return;
        }

        PromptBox.Text = SelectedEntry.Prompt;
        ConstraintsBox.Text = SelectedEntry.Constraints;
        NegativeBox.Text = SelectedEntry.Negative;
        ApplySettingsToControls(SelectedEntry.Settings);
        FooterStatus = $"Applied viewed item to the form: {SelectedEntry.OutputFileName}";
    }

    private void ApplySettingsToControls(GenerationSettings settings)
    {
        ModelCombo.SelectedItem = settings.Model;
        ApplyModelRules();
        ModeCombo.SelectedItem = settings.Mode;
        ApplyModeRules();
        SizeCombo.SelectedItem = settings.Size;
        QualityCombo.SelectedItem = settings.Quality;
        BackgroundCombo.SelectedItem = settings.Background;
        ApplyBackgroundRules();
        OutputCombo.SelectedItem = settings.OutputFormat;
        FidelityCombo.SelectedItem = settings.InputFidelity;
        AugmentCombo.SelectedItem = settings.Augment;

        _referenceImagePath = settings.Reference;
        ReferenceLabel = ShortenPath(_referenceImagePath);
        AutoGrowTextBox(PromptBox);
        AutoGrowTextBox(ConstraintsBox);
        AutoGrowTextBox(NegativeBox);
    }

    private void EditViewed_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry?.HasGeneratedFile != true)
        {
            return;
        }

        if (!ImageGenerationService.SupportsImageEdit(Selected(ModelCombo, "gpt-image-1.5")))
        {
            MessageBox.Show(this, "The selected model does not support image editing.", "Cannot edit image", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ModeCombo.SelectedItem = ModeEdit;
        PromptBox.Clear();
        ConstraintsBox.Clear();
        NegativeBox.Clear();
        _referenceImagePath = SelectedEntry.Output;
        ReferenceLabel = ShortenPath(_referenceImagePath);
        ApplyModeRules();
        FooterStatus = $"Ready to edit: {SelectedEntry.OutputFileName}. Prompt can stay empty.";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = SelectedProject?.GeneratedDirectory ?? AppPaths.DefaultGeneratedDirectory;
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = SelectedProject?.RootDirectory ?? AppPaths.RootDirectory;
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project == null)
        {
            return;
        }

        if (project.IsDefault)
        {
            MessageBox.Show(this, "The default project cannot be removed.", "Remove project", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_runningJobs.Values.Any(job => job.Project.Id == project.Id))
        {
            MessageBox.Show(this, "Cancel or wait for this project's running jobs before removing it.", "Project is running", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Remove project \"{project.Name}\"? This deletes its project folder, history, and generated files.", "Remove project", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var removeIndex = Projects.IndexOf(project);
        try
        {
            if (Directory.Exists(project.RootDirectory))
            {
                Directory.Delete(project.RootDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not remove project", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Projects.Remove(project);
        var nextProject = Projects.Count == 0
            ? null
            : Projects[Math.Clamp(removeIndex, 0, Projects.Count - 1)];
        if (nextProject != null)
        {
            _isSwitchingProject = true;
            ProjectCombo.SelectedItem = nextProject;
            _isSwitchingProject = false;
            SwitchProject(nextProject);
        }
        else
        {
            SelectedProject = null;
            History.Clear();
            SaveProjects();
        }

        FooterStatus = $"Removed project: {project.Name}";
    }

    private void ProjectCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isSwitchingProject || ProjectCombo.SelectedItem is not ProjectInfo project || SelectedProject == project)
        {
            return;
        }

        SwitchProject(project);
    }

    private void SwitchProject(ProjectInfo project)
    {
        SaveHistory();
        SelectedProject = project;
        SaveProjects();
        LoadHistoryForSelectedProject();
        if (History.FirstOrDefault() is { } first)
        {
            ViewEntry(first);
        }
        else
        {
            SelectedEntry = null;
            ClearPreviewImage();
            PreviewMessage = "This project has no history yet. Generate a new image to start it.";
            SelectedLog = "";
        }

        FooterStatus = HistoryStore.LastLoadWarning ?? $"Project: {project.Name} | History: {project.HistoryPath}";
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForProjectName();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var project = ProjectStore.CreateProject(name, Projects);
        Projects.Add(project);
        _isSwitchingProject = true;
        ProjectCombo.SelectedItem = project;
        _isSwitchingProject = false;
        SwitchProject(project);
    }

    private string? PromptForProjectName()
    {
        var dialog = new Window
        {
            Title = "New Project",
            Owner = this,
            Width = 380,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.Black,
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Project name",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 6),
        });
        var box = new System.Windows.Controls.TextBox
        {
            MinHeight = 30,
            Text = $"Project {Projects.Count + 1}",
        };
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        panel.Children.Add(box);

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Width = 84, Margin = new Thickness(0, 0, 8, 0) };
        var create = new System.Windows.Controls.Button { Content = "Create", Width = 84, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        create.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        box.Focus();

        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private void BrowseReference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose reference image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp|PNG|*.png|JPEG|*.jpg;*.jpeg|WebP|*.webp|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _referenceImagePath = dialog.FileName;
            ReferenceLabel = ShortenPath(_referenceImagePath);
        }
    }

    private void ClearReference_Click(object sender, RoutedEventArgs e)
    {
        _referenceImagePath = null;
        ReferenceLabel = "No file selected";
    }

    private void ModelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyModelRules();
        }
    }

    private void ModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyModeRules();
        }
    }

    private void BackgroundCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyBackgroundRules();
        }
    }

    private void ApplyModelRules()
    {
        var model = Selected(ModelCombo, "gpt-image-1.5");
        if (model == "gpt-image-2")
        {
            SizeCombo.ItemsSource = Gpt2Sizes;
            if (SizeCombo.SelectedItem is not string selectedSize || !Gpt2Sizes.Contains(selectedSize))
            {
                SizeCombo.SelectedItem = "auto";
            }

            BackgroundCombo.ItemsSource = new[] { "opaque", "auto" };
            if ((BackgroundCombo.SelectedItem as string) == "transparent")
            {
                BackgroundCombo.SelectedItem = "opaque";
            }

            FidelityCombo.IsEnabled = false;
        }
        else
        {
            SizeCombo.ItemsSource = LegacySizes;
            if (SizeCombo.SelectedItem is not string selectedSize || !LegacySizes.Contains(selectedSize))
            {
                SizeCombo.SelectedItem = "1024x1024";
            }

            BackgroundCombo.ItemsSource = new[] { "transparent", "opaque", "auto" };
            FidelityCombo.IsEnabled = Selected(ModeCombo, ModeGenerate) == ModeEdit;
        }

        ApplyBackgroundRules();
        ApplyModeRules();
    }

    private void ApplyBackgroundRules()
    {
        if (Selected(BackgroundCombo, "transparent") == "transparent")
        {
            OutputCombo.ItemsSource = new[] { "png", "webp" };
            if ((OutputCombo.SelectedItem as string) == "jpeg")
            {
                OutputCombo.SelectedItem = "png";
            }
        }
        else
        {
            OutputCombo.ItemsSource = new[] { "png", "webp", "jpeg" };
        }

        OutputCombo.SelectedItem ??= "png";
    }

    private void ApplyModeRules()
    {
        var isEdit = Selected(ModeCombo, ModeGenerate) == ModeEdit;
        BrowseReferenceButton.IsEnabled = isEdit;
        ClearReferenceButton.IsEnabled = isEdit;
        FidelityCombo.IsEnabled = isEdit && Selected(ModelCombo, "gpt-image-1.5") != "gpt-image-2";
    }

    private static string ShortenPath(string? path, int maxChars = 54)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "No file selected";
        }

        return path.Length <= maxChars ? path : "..." + path[^Math.Max(0, maxChars - 3)..];
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _budgetTimer.Stop();
        _shutdownCts.Cancel();

        foreach (var runningJob in _runningJobs.Values.ToList())
        {
            runningJob.CancellationTokenSource.Cancel();
            runningJob.Entry.Status = JobStatus.Canceled;
            runningJob.Entry.Error = "Canceled because the app was closed.";
            runningJob.Entry.Finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveProjectEntry(runningJob.Project, runningJob.Entry);
        }

        SaveHistory();
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        RegenerateViewed();
    }

    private void RegenerateViewed()
    {
        var entry = SelectedEntry;
        if (entry == null)
        {
            return;
        }

        var project = SelectedProject;
        if (project == null)
        {
            return;
        }

        var settings = CloneSettings(entry.Settings);
        if (_generator.ValidateForRun(settings) is { } validationError)
        {
            MessageBox.Show(this, validationError, "Cannot regenerate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entryId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(project.GeneratedDirectory);
        var outputPath = NextOutputPath(project, entry.Prompt, settings.OutputFormat, entryId);
        var logPreview = _generator.BuildLogPreview(entry.Prompt, entry.Constraints, entry.Negative, outputPath, settings);
        var newEntry = new HistoryEntry
        {
            Id = entryId,
            ProjectId = project.Id,
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Status = JobStatus.InProgress,
            Prompt = entry.Prompt,
            Constraints = entry.Constraints,
            Negative = entry.Negative,
            Output = outputPath,
            Settings = settings,
            Log = "Running (regenerate):\n" + logPreview + "\n\n",
        };

        History.Insert(0, newEntry);
        while (History.Count > HistoryStore.MaxEntries)
        {
            History.RemoveAt(History.Count - 1);
        }

        SaveHistory();
        ViewEntry(newEntry);
        FooterStatus = $"Regenerating: {newEntry.OutputFileName}";
        _ = RunGenerationAsync(project, newEntry, entry.Prompt, entry.Constraints, entry.Negative, outputPath);
    }

    private static GenerationSettings CloneSettings(GenerationSettings source)
    {
        return new GenerationSettings
        {
            Model = source.Model,
            Mode = source.Mode,
            Size = source.Size,
            Quality = source.Quality,
            Background = source.Background,
            OutputFormat = source.OutputFormat,
            InputFidelity = source.InputFidelity,
            Augment = source.Augment,
            Reference = source.Reference,
        };
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry?.HasGeneratedFile != true)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedEntry.Output}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Show in Explorer failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Generate_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            RegenerateViewed();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && HistoryList.IsKeyboardFocusWithin && HistoryList.SelectedItems.Count > 0)
        {
            DeleteSelectedHistory_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsDroppedImageFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!IsDroppedImageFile(e, out var path))
        {
            return;
        }

        _referenceImagePath = path;
        ReferenceLabel = ShortenPath(_referenceImagePath);
        if (Selected(ModeCombo, ModeGenerate) != ModeEdit
            && ImageGenerationService.SupportsImageEdit(Selected(ModelCombo, "gpt-image-1.5")))
        {
            ModeCombo.SelectedItem = ModeEdit;
            ApplyModeRules();
        }

        FooterStatus = $"Reference set: {Path.GetFileName(path)}";
        e.Handled = true;
    }

    private static bool IsDroppedImageFile(DragEventArgs e)
    {
        return IsDroppedImageFile(e, out _);
    }

    private static bool IsDroppedImageFile(DragEventArgs e, out string? path)
    {
        path = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return false;
        }

        var candidate = files[0];
        var ext = Path.GetExtension(candidate).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            path = candidate;
            return true;
        }

        return false;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
