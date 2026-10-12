using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LocalTranscriber;

public partial class MainWindow : Window
{
    private readonly Store store = new();
    private readonly ModelManager models;
    private readonly Processes processes = new();
    private readonly Pipeline pipeline;
    private readonly Summarizer summarizer;
    private readonly DispatcherTimer playback = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? operation;
    private Workspace? workspace;
    private Media? selectedMedia;
    private Job? selectedJob;
    private Transcript? transcript;
    private Summary? summary;
    private bool ready, dirty, summaryDirty, playing, switchingVersion;
    private int shownVersion;
    private readonly Stack<Transcript> undo = new(), redo = new();
    public MainWindow()
    {
        InitializeComponent();
        models = new(store); models.LoadImports(); pipeline = new(store, models, processes); summarizer = new(models, processes, pipeline);
        pipeline.Changed += job => Dispatcher.BeginInvoke(() => { if (!ready) return; StageText.Text = job.SourceName + " · " + job.Stage; JobProgress.Value = Math.Clamp(job.CompletedSeconds / job.Source.Duration * 100, 0, 100); JobProgress.IsIndeterminate = job.State is JobState.Preparing or JobState.Aligning or JobState.Saving; RefreshJobs(); if (job.State == JobState.Completed) Status.Text = $"{job.SourceName} completed. Open the result from Library history."; });
        ready = true;
        SystemPromptBox.Text = new SummarySettings().SystemPrompt;
        ThreadsBox.Text = store.Settings.Threads.ToString(CultureInfo.InvariantCulture);
        ThemeBox.SelectedIndex = Array.IndexOf(new[] { "System", "Light", "Dark" }, store.Settings.Theme);
        ApplyTheme(); RefreshWorkspaces(); RefreshModels(); ShowDevice();
        InitializeDictation();
        playback.Tick += (_, _) => { if (Player.NaturalDuration.HasTimeSpan && !SeekSlider.IsMouseCaptureWithin) { SeekSlider.Value = Player.Position.TotalSeconds; PlaybackTime.Text = Player.Position.ToString(@"hh\:mm\:ss"); } };
        playback.Start();
        PreviewKeyDown += (_, args) => { if (Keyboard.Modifiers == ModifierKeys.Control) { if (args.Key == Key.O) AddFiles(this, new RoutedEventArgs()); else if (args.Key == Key.S) SaveTranscript(this, new RoutedEventArgs()); else if (args.Key == Key.F) { Navigation.SelectedIndex = 2; TranscriptSearch.Focus(); } } };
        foreach (var job in store.Jobs().Where(j => j.State == JobState.Queued)) pipeline.Queue(job);
    }
    private static string Choice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static int Number(TextBox box, int min, int max) => int.TryParse(box.Text, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max ? value : throw new IOException($"Enter a value between {min} and {max}.");
    private static double Decimal(TextBox box, double min, double max) => double.TryParse(box.Text, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= min && value <= max ? value : throw new IOException($"Enter a value between {min} and {max}.");
    private void Note(string message) => Status.Text = message;
    private async Task Guard(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { Note("Canceled. Incomplete downloads and valid job checkpoints can be resumed."); } catch (Exception error) { Note(error.Message); MessageBox.Show(this, error.Message, "Local Transcriber", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private CancellationToken BeginOperation()
    {
        if (operation != null) throw new IOException("Wait for the current download or summary, or cancel it first.");
        operation = new CancellationTokenSource(); return operation.Token;
    }
    private void EndOperation() { operation?.Dispose(); operation = null; RefreshModels(); }
    private void RequireIdle() { if (microphone != null || pipeline.Busy || operation != null || store.Jobs().Any(j => j.State == JobState.Queued)) throw new IOException("Stop recording and wait for running or queued jobs and downloads first."); }
    private Workspace RequireWorkspace() => workspace ?? throw new IOException("Add or select a workspace first.");
    private Media RequireMedia() => selectedMedia ?? throw new IOException("Select a recording in Library first.");
    private Job RequireResult() => selectedJob ?? throw new IOException("Open a completed result from Library history.");
    private string? PickFolder(string title) { var dialog = new OpenFolderDialog { Title = title }; return dialog.ShowDialog(this) == true ? dialog.FolderName : null; }
    private static void Open(string path) { if (File.Exists(path) || Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else throw new IOException("The folder or document is unavailable."); }
    private void RefreshWorkspaces(string? id = null)
    {
        var list = store.Workspaces(); WorkspaceBox.ItemsSource = list; WorkspaceBox.SelectedItem = list.FirstOrDefault(w => w.Id == (id ?? workspace?.Id)) ?? list.FirstOrDefault();
        if (list.Count == 0) { workspace = null; MediaGrid.ItemsSource = null; JobsGrid.ItemsSource = null; }
    }
    private void RefreshJobs() { var id = (JobsGrid.SelectedItem as Job)?.Id; var list = workspace == null ? [] : store.Jobs(workspace.Id).OrderByDescending(j => j.CreatedAt).ToList(); JobsGrid.ItemsSource = list; JobsGrid.SelectedItem = list.FirstOrDefault(j => j.Id == id); }
    private void RefreshMedia() { if (!ready || workspace == null) return; var id = selectedMedia?.Id; var list = store.Media(workspace.Id).Where(m => m.Name.Contains(LibrarySearch.Text, StringComparison.OrdinalIgnoreCase)).ToList(); MediaGrid.ItemsSource = list; MediaGrid.SelectedItem = list.FirstOrDefault(m => m.Id == id); }
    private void RefreshModels()
    {
        if (!ready) return;
        var speech = (SpeechModelBox.SelectedItem as Model)?.Id; var llm = (SummaryModelBox.SelectedItem as Model)?.Id;
        foreach (var model in models.Catalogue) model.InstalledStatus = !model.Available ? "Unavailable" : models.Installed(model) ? "Installed" : "Not installed";
        var asr = models.Catalogue.Where(m => m.Role == "asr" && m.Available && models.Installed(m)).ToList(); SpeechModelBox.ItemsSource = asr; SpeechModelBox.SelectedItem = asr.FirstOrDefault(m => m.Id == speech) ?? asr.FirstOrDefault();
        var summaries = models.Catalogue.Where(m => m.Role == "llm" && m.Available && models.Installed(m)).ToList(); SummaryModelBox.ItemsSource = summaries; SummaryModelBox.SelectedItem = summaries.FirstOrDefault(m => m.Id == llm) ?? summaries.FirstOrDefault(m => m.Id == "lfm25-1.2b-instruct") ?? summaries.FirstOrDefault();
        FilterModels(this, new RoutedEventArgs());
        ModelSelected(this, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        ModelUsage.Text = $"Installed: {models.Catalogue.Where(models.Installed).Sum(m => m.DownloadSize) / 1073741824d:N2} GB";
        StorageInfo.Text = $"Models: {store.Settings.ModelRoot}\nCache: {store.Settings.CacheRoot}\nApp index: {store.Root}";
        RefreshDictationModels();
    }
    private async void AddWorkspace(object sender, RoutedEventArgs e) => await Guard(() => { var path = PickFolder("Add workspace"); if (path != null) { var added = store.AddWorkspace(path); RefreshWorkspaces(added.Id); Note("Workspace pointer added. Existing files were not copied."); } return Task.CompletedTask; });
    private void WorkspaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || WorkspaceBox.SelectedItem is not Workspace selected) return;
        workspace = selected; RefreshMedia(); RefreshJobs();
        var glossary = Path.Combine(store.Root, "glossaries", selected.Id + ".txt"); ContextBox.Text = File.Exists(glossary) ? File.ReadAllText(glossary) : "";
        Note(selected.Available ? selected.Path : "Folder unavailable. Use Locate folder, Refresh, or Remove pointer.");
    }
    private async void RemoveWorkspace(object sender, RoutedEventArgs e) => await Guard(() => { var w = RequireWorkspace(); if (MessageBox.Show(this, $"Remove the shortcut to {w.Label}?\n\nThe recording files, saved results and exports will stay on disk.", "Remove workspace pointer", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { store.RemovePointer(w.Id); RefreshWorkspaces(); } return Task.CompletedTask; });
    private async void OpenWorkspace(object sender, RoutedEventArgs e) => await Guard(() => { Open(RequireWorkspace().Path); return Task.CompletedTask; });
    private async void RenameWorkspace(object sender, RoutedEventArgs e) => await Guard(() => { var w = RequireWorkspace(); var name = TextPrompt("Workspace label", w.Label); if (!string.IsNullOrWhiteSpace(name)) { store.UpdateWorkspace(w with { Label = name.Trim() }); RefreshWorkspaces(w.Id); } return Task.CompletedTask; });
    private async void ReorderWorkspace(object sender, RoutedEventArgs e) => await Guard(() => { var w = RequireWorkspace(); var list = store.Workspaces(); var index = list.FindIndex(x => x.Id == w.Id); if (index > 0) { (list[index], list[index - 1]) = (list[index - 1], list[index]); for (var i = 0; i < list.Count; i++) store.UpdateWorkspace(list[i] with { Position = i }); RefreshWorkspaces(w.Id); } return Task.CompletedTask; });
    private async void LocateWorkspace(object sender, RoutedEventArgs e) => await Guard(() => { var w = RequireWorkspace(); var path = PickFolder("Locate workspace folder"); if (path != null) { store.UpdateWorkspace(w with { Path = path }); RefreshWorkspaces(w.Id); Note("Pointer relocated. Existing recording links keep their original paths; re-add moved files."); } return Task.CompletedTask; });
    private void RefreshLibrary(object sender, RoutedEventArgs e) { if (!ready) return; RefreshWorkspaces(); RefreshMedia(); RefreshJobs(); }
    private void SearchLibrary(object sender, TextChangedEventArgs e) => RefreshMedia();
    private async Task ImportFiles(IEnumerable<string> files, bool copy)
    {
        var w = RequireWorkspace(); if (!w.Available) throw new IOException("The workspace folder is unavailable.");
        foreach (var source in files)
        {
            Note("Probing " + Path.GetFileName(source));
            var media = await pipeline.Probe(source, w.Id, CancellationToken.None);
            if (copy && !string.Equals(Path.GetDirectoryName(source), w.Path, StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(w.Path, Path.GetFileName(source)); if (File.Exists(target)) target = Path.Combine(w.Path, Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(source));
                var part = target + ".partial";
                try { await using (var input = File.OpenRead(source)) await using (var output = File.Create(part)) await input.CopyToAsync(output); File.Move(part, target); }
                finally { if (File.Exists(part)) File.Delete(part); }
                media = await pipeline.Probe(target, w.Id, CancellationToken.None);
            }
            var existing = store.Media(w.Id).FirstOrDefault(m => string.Equals(m.Path, media.Path, StringComparison.OrdinalIgnoreCase)); store.SaveMedia(existing == null ? media : media with { Id = existing.Id }); selectedMedia = existing == null ? media : media with { Id = existing.Id };
        }
        RefreshMedia(); Note("Media imported. Select the speech model and transcribe.");
    }
    private async Task PickFiles(bool copy) { var dialog = new OpenFileDialog { Title = copy ? "Import copies to workspace" : "Add recordings as links", Multiselect = true, Filter = "Audio and video|*.wav;*.mp3;*.mp4;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma;*.aif;*.aiff;*.webm;*.mkv;*.mov;*.avi;*.wmv;*.amr;*.m4v;*.mpeg;*.mpg;*.ts|All files|*.*" }; if (dialog.ShowDialog(this) == true) await ImportFiles(dialog.FileNames, copy); }
    private async void AddFiles(object sender, RoutedEventArgs e) => await Guard(() => PickFiles(false));
    private async void CopyFiles(object sender, RoutedEventArgs e) => await Guard(() => PickFiles(true));
    private async void FilesDropped(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await Guard(() => ImportFiles(files, false)); }
    private void MediaSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || MediaGrid.SelectedItem is not Media media) return;
        selectedMedia = media; SourceInfo.Text = $"{media.Name}\n{media.DurationText} · {media.Size / 1048576d:N1} MB · {media.Tracks.Count} audio track(s)\n{media.Path}"; TrackBox.ItemsSource = media.Tracks; TrackBox.SelectedIndex = 0;
    }
    private void OpenTranscribe(object sender, MouseButtonEventArgs e) { if (selectedMedia != null) Navigation.SelectedIndex = 1; }
    private void SpeechModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (SpeechModelBox.SelectedItem is not Model model) { SpeechCapability.Text = "No speech model installed. Download one in Models."; LanguageBox.ItemsSource = new[] { "auto" }; LanguageBox.SelectedIndex = 0; return; }
        SpeechCapability.Text = model.Description + "\n" + Hardware.Rating(model) + (model.ForcedLanguage ? "" : "\nThis model chooses the spoken language automatically; language forcing and vocabulary hints are unavailable.");
        LanguageBox.ItemsSource = model.ForcedLanguage ? new[] { "auto", "en", "fr" }.Concat(model.Languages.Except(new[] { "en", "fr" }).Order()).ToArray() : new[] { "auto" }; LanguageBox.SelectedIndex = 0; HintsBox.IsEnabled = model.ForcedLanguage;
    }
    private Job MakeJob(Media media)
    {
        var model = SpeechModelBox.SelectedItem as Model ?? throw new IOException("Choose or download a speech model in Models.");
        var track = media.Id == selectedMedia?.Id ? (TrackBox.SelectedItem as AudioTrack)?.Index ?? media.Tracks[0].Index : media.Tracks[0].Index;
        return pipeline.Enqueue(RequireWorkspace(), media, model, track, LanguageBox.SelectedItem?.ToString() ?? "auto", Choice(CleanupBox), HintsBox.Text, Decimal(VadBox, .1, .9));
    }
    private async void Transcribe(object sender, RoutedEventArgs e) => await Guard(() => { MakeJob(RequireMedia()); RefreshJobs(); Note("Transcription queued. View progress here or in Library history."); return Task.CompletedTask; });
    private async void TranscribeBatch(object sender, RoutedEventArgs e) => await Guard(() => { var items = MediaGrid.SelectedItems.Cast<Media>().ToList(); if (items.Count == 0) throw new IOException("Select multiple files in Library first."); foreach (var media in items) MakeJob(media); RefreshJobs(); Note($"Queued {items.Count} recordings; one CPU model runs at a time."); return Task.CompletedTask; });
    private void CancelJob(object sender, RoutedEventArgs e) { if (JobsGrid.SelectedItem is Job job) pipeline.Cancel(job.Id); }
    private void CancelActive(object sender, RoutedEventArgs e) { foreach (var job in store.Jobs().Where(j => j.State is not (JobState.Completed or JobState.Failed or JobState.Canceled or JobState.Interrupted or JobState.Queued))) pipeline.Cancel(job.Id); }
    private async void ResumeJob(object sender, RoutedEventArgs e) => await Guard(() => { var job = JobsGrid.SelectedItem as Job ?? throw new IOException("Select an interrupted, canceled or failed job."); if (job.State is JobState.Interrupted or JobState.Canceled or JobState.Failed) pipeline.Resume(job); else throw new IOException("This job does not need to resume."); RefreshJobs(); return Task.CompletedTask; });
    private bool AllowDiscard() => (!dirty && !summaryDirty) || MessageBox.Show(this, "There are unsaved transcript or summary changes. Discard them?", "Unsaved changes", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    private async void OpenJob(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        var job = JobsGrid.SelectedItem as Job ?? throw new IOException("Select a completed transcription in Library.");
        if (!File.Exists(Path.Combine(job.Directory, "transcript.raw.json"))) throw new IOException(job.Error ?? "This job has no completed result yet. Resume it or transcribe again.");
        if (!AllowDiscard()) return Task.CompletedTask;
        selectedJob = job; selectedMedia = job.Source; SourceInfo.Text = $"{job.Source.Name}\n{job.Source.DurationText} · {job.Source.Path}"; TrackBox.ItemsSource = job.Source.Tracks; TrackBox.SelectedItem = job.Source.Tracks.First(t => t.Index == job.TrackIndex); transcript = Store.LoadTranscript(job, VersionBox.SelectedIndex == 1); dirty = summaryDirty = false; undo.Clear(); redo.Clear(); ShowTranscript();
        var summaryPath = Path.Combine(job.Directory, "summary.json"); summary = File.Exists(summaryPath) ? JsonFiles.Read<Summary>(summaryPath) : null; SummaryEditor.Text = summary?.Text ?? ""; summaryDirty = false;
        SetMedia(job.Source.Path); Navigation.SelectedIndex = 2; return Task.CompletedTask;
    });
    private void ShowTranscript()
    {
        var selectedId = (TranscriptGrid.SelectedItem as Segment)?.SegmentId;
        TranscriptGrid.ItemsSource = transcript?.Segments.Where(s => s.Text.Contains(TranscriptSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selectedId != null) TranscriptGrid.SelectedItem = TranscriptGrid.Items.Cast<Segment>().FirstOrDefault(s => s.SegmentId == selectedId);
        TranscriptGrid.IsReadOnly = VersionBox.SelectedIndex == 1;
        UpdateTranscriptText();
    }
    private void UpdateTranscriptText()
    {
        TranscriptState.Text = transcript == null ? "Open a result from Library" : selectedJob!.SourceName + " · " + transcript.Version + (dirty ? " · unsaved changes" : " · saved");
        SourceTranscript.Text = transcript?.FullText ?? "";
    }
    private void SearchTranscript(object sender, TextChangedEventArgs e) { if (ready) ShowTranscript(); }
    private void TranscriptVersionChanged(object sender, SelectionChangedEventArgs e) { if (!ready || switchingVersion) return; if (selectedJob == null) { shownVersion = VersionBox.SelectedIndex; return; } if (!AllowDiscard()) { switchingVersion = true; VersionBox.SelectedIndex = shownVersion; switchingVersion = false; return; } shownVersion = VersionBox.SelectedIndex; transcript = Store.LoadTranscript(selectedJob, shownVersion == 1); dirty = false; undo.Clear(); redo.Clear(); ShowTranscript(); }
    private static Transcript Clone(Transcript value) => JsonSerializer.Deserialize<Transcript>(JsonSerializer.Serialize(value, JsonFiles.Options), JsonFiles.Options)!;
    private void TranscriptEdited(object sender, DataGridCellEditEndingEventArgs e) { if (transcript == null || e.EditAction != DataGridEditAction.Commit) return; if (undo.Count >= 32) undo.Clear(); undo.Push(Clone(transcript)); redo.Clear(); dirty = true; Dispatcher.BeginInvoke(UpdateTranscriptText, DispatcherPriority.Background); }
    private async void SaveTranscript(object sender, RoutedEventArgs e) => await Guard(() => { if (transcript == null) throw new IOException("Open a transcript first."); if (VersionBox.SelectedIndex == 1) throw new IOException("Choose Edited / latest or Revert to raw before saving edits."); TranscriptGrid.CommitEdit(DataGridEditingUnit.Cell, true); TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true); Store.SaveEdited(RequireResult(), transcript); dirty = false; ShowTranscript(); Note("Edited version saved. Raw ASR output is unchanged."); return Task.CompletedTask; });
    private void UndoTranscript(object sender, RoutedEventArgs e) { if (transcript != null && undo.TryPop(out var previous)) { redo.Push(Clone(transcript)); transcript = previous; dirty = true; ShowTranscript(); } }
    private void RedoTranscript(object sender, RoutedEventArgs e) { if (transcript != null && redo.TryPop(out var next)) { undo.Push(Clone(transcript)); transcript = next; dirty = true; ShowTranscript(); } }
    private void RevertTranscript(object sender, RoutedEventArgs e) { if (selectedJob == null || !AllowDiscard()) return; if (transcript != null) undo.Push(Clone(transcript)); redo.Clear(); switchingVersion = true; VersionBox.SelectedIndex = shownVersion = 0; switchingVersion = false; transcript = Store.LoadTranscript(selectedJob, true); transcript.Version = "edited"; dirty = true; ShowTranscript(); }
    private void SetMedia(string path) { Player.Stop(); Player.Source = new Uri(path); playing = false; Player.Position = TimeSpan.Zero; }
    private async void PlaySource(object sender, RoutedEventArgs e) => await Guard(() => { SetMedia(RequireMedia().Path); Navigation.SelectedIndex = 2; Player.Play(); playing = true; return Task.CompletedTask; });
    private void TogglePlay(object sender, RoutedEventArgs e) { if (Player.Source == null) return; if (playing) Player.Pause(); else Player.Play(); playing = !playing; }
    private void StopPlaying(object sender, RoutedEventArgs e) { Player.Stop(); playing = false; }
    private void MediaOpened(object sender, RoutedEventArgs e) { if (Player.NaturalDuration.HasTimeSpan) SeekSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds; }
    private void MediaFailed(object sender, ExceptionRoutedEventArgs e) { playing = false; Note("Windows cannot preview this codec. Use Compatible audio preview; transcription through FFmpeg remains available."); }
    private void SeekMedia(object sender, MouseButtonEventArgs e) { if (Player.Source != null) Player.Position = TimeSpan.FromSeconds(SeekSlider.Value); }
    private void SeekSegment(object sender, SelectionChangedEventArgs e) { if (TranscriptGrid.SelectedItem is Segment s && Player.Source != null) Player.Position = TimeSpan.FromMilliseconds(s.StartMs); }
    private void SeekTranscriptClick(object sender, MouseButtonEventArgs e) { if (TranscriptGrid.SelectedItem is Segment s && Player.Source != null) Player.Position = TimeSpan.FromMilliseconds(s.StartMs); }
    private async Task Preview(bool enhanced)
    {
        var token = BeginOperation(); try { var media = RequireMedia(); var file = await pipeline.Preview(media, (TrackBox.SelectedItem as AudioTrack)?.Index ?? media.Tracks[0].Index, enhanced, token); SetMedia(file); Navigation.SelectedIndex = 2; Player.Play(); playing = true; Note(enhanced ? "Playing enhanced 15-second preview. Timestamps here refer to this preview only." : "Playing original 15-second preview."); } finally { EndOperation(); }
    }
    private async void PreviewOriginal(object sender, RoutedEventArgs e) => await Guard(() => Preview(false));
    private async void PreviewEnhanced(object sender, RoutedEventArgs e) => await Guard(() => Preview(true));
    private async void CompatiblePreview(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var media = selectedJob?.Source ?? RequireMedia(); var track = selectedJob?.TrackIndex ?? (TrackBox.SelectedItem as AudioTrack)?.Index ?? media.Tracks[0].Index; var token = BeginOperation();
        try { var path = Path.Combine(store.Settings.CacheRoot, $"playback-{media.Id}-{track}-{media.ModifiedTicks}.wav"); if (!File.Exists(path)) { if (new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace < media.Duration * 32000 + 256L * 1048576) throw new IOException("Not enough cache space for audio playback. Change cache folder or free disk space."); Note("Preparing full compatible audio preview…"); await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), ["-nostdin", "-v", "error", "-protocol_whitelist", "file,pipe", "-i", media.Path, "-map", $"0:{track}", "-vn", "-ac", "1", "-ar", "16000", "-y", path + ".part.wav"], token); File.Move(path + ".part.wav", path, true); } SetMedia(path); Player.Play(); playing = true; Note("Playing decoded audio with the original recording's timeline."); } finally { EndOperation(); }
    });
    private string? SavePath(string basename, string extension, string title)
    {
        var dir = selectedJob == null ? RequireWorkspace().Path : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(selectedJob.Directory))!, "exports", Path.GetFileNameWithoutExtension(selectedJob.Source.Name)); Directory.CreateDirectory(dir);
        var dialog = new SaveFileDialog { Title = title, InitialDirectory = dir, FileName = basename + "." + extension, Filter = $"{extension.ToUpperInvariant()} file|*.{extension}", DefaultExt = extension, OverwritePrompt = true }; return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private async void ExportTranscript(object sender, RoutedEventArgs e) => await Guard(() => { if (transcript == null) throw new IOException("Open a transcript first."); TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true); var format = Choice(ExportFormat); var path = SavePath("transcript", format, "Export transcript"); if (path != null) { Exporter.Save(path, Exporter.Transcript(transcript, format, (TimestampStyle)TimestampBox.SelectedIndex, SpeakersToggle.IsChecked == true), format == "csv" && CsvBom.IsChecked == true); Note("Transcript exported: " + path); } return Task.CompletedTask; });
    private async void OpenSourceFolder(object sender, RoutedEventArgs e) => await Guard(() => { Open(Path.GetDirectoryName((selectedJob?.Source ?? RequireMedia()).Path)!); return Task.CompletedTask; });
    private async void OpenOutputFolder(object sender, RoutedEventArgs e) => await Guard(() => { Open(RequireResult().Directory); return Task.CompletedTask; });
    private void SummaryModelChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; if (SummaryModelBox.SelectedItem is Model model) { SummaryCapability.Text = model.Description + "\n" + Hardware.Rating(model); ContextTokensBox.Text = Hardware.Context(model).ToString(CultureInfo.InvariantCulture); } else SummaryCapability.Text = "Download a summary model in Models."; }
    private SummarySettings ReadSummarySettings() => new() { Context = ContextBox.Text, Template = Choice(TemplateBox), CustomTemplate = CustomTemplateBox.Text, Language = Choice(SummaryLanguageBox), Threads = store.Settings.Threads, ContextTokens = Number(ContextTokensBox, 1024, 8192), OutputTokens = Number(OutputTokensBox, 64, 2048), Temperature = Decimal(TemperatureBox, 0, 2), TopP = Decimal(TopPBox, .01, 1), TopK = Number(TopKBox, 1, 200), RepeatPenalty = Decimal(RepeatBox, 1, 2), BatchSize = Number(BatchBox, 16, 512), Seed = Number(SeedBox, 0, int.MaxValue), SystemPrompt = SystemPromptBox.Text };
    private async void GenerateSummary(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var job = RequireResult(); if (transcript == null) throw new IOException("Open a transcript first."); var model = SummaryModelBox.SelectedItem as Model ?? throw new IOException("Choose or download a summary model.");
        TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true); if (dirty) { Store.SaveEdited(job, transcript); dirty = false; ShowTranscript(); }
        var token = BeginOperation(); GenerateButton.IsEnabled = false;
        try { summary = await summarizer.Generate(job, Clone(transcript), model, ReadSummarySettings(), message => Dispatcher.BeginInvoke(() => SummaryStage.Text = message), token); SummaryEditor.Text = summary.Text; summaryDirty = false; SummaryStage.Text = "Summary saved. " + string.Join(" ", summary.Warnings); Note("Local summary complete."); }
        finally { GenerateButton.IsEnabled = true; EndOperation(); }
    });
    private void ResetSummarySettings(object sender, RoutedEventArgs e) { ContextTokensBox.Text = "4096"; OutputTokensBox.Text = "768"; TemperatureBox.Text = "0.1"; TopPBox.Text = "0.9"; TopKBox.Text = "50"; RepeatBox.Text = "1.1"; BatchBox.Text = "256"; SeedBox.Text = "42"; SystemPromptBox.Text = new SummarySettings().SystemPrompt; }
    private void SummaryEdited(object sender, TextChangedEventArgs e) { if (ready && summary != null) summaryDirty = true; }
    private async void SaveSummary(object sender, RoutedEventArgs e) => await Guard(() => { if (summary == null) throw new IOException("Generate or open a summary first."); var job = RequireResult(); summary.Text = SummaryEditor.Text; JsonFiles.Write(Path.Combine(job.Directory, "summaries", summary.Id, $"summary.edited-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.json"), summary); JsonFiles.Write(Path.Combine(job.Directory, "summary.json"), summary); summaryDirty = false; Note("Summary edit version saved; generated original is preserved."); return Task.CompletedTask; });
    private async void ExportSummary(object sender, RoutedEventArgs e) => await Guard(() => { if (summary == null) throw new IOException("Generate a summary first."); summary.Text = SummaryEditor.Text; var format = Choice(SummaryFormat); var path = SavePath("summary", format, "Export summary"); if (path != null) { Exporter.Save(path, format == "json" ? JsonSerializer.Serialize(summary, JsonFiles.Options) : summary.Text); Note("Summary exported: " + path); } return Task.CompletedTask; });
    private async void SaveGlossary(object sender, RoutedEventArgs e) => await Guard(() => { var w = RequireWorkspace(); var path = Path.Combine(store.Root, "glossaries", w.Id + ".txt"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); Exporter.Save(path, ContextBox.Text); Note("Workspace context and terminology saved."); return Task.CompletedTask; });
    private async void ImportGlossary(object sender, RoutedEventArgs e) => await Guard(() => { var dialog = new OpenFileDialog { Filter = "Text glossary|*.txt;*.md;*.csv" }; if (dialog.ShowDialog(this) == true) { if (new FileInfo(dialog.FileName).Length > 128000) throw new IOException("Choose a glossary smaller than 128 KB."); ContextBox.Text = File.ReadAllText(dialog.FileName); } return Task.CompletedTask; });
    private async void ExportGlossary(object sender, RoutedEventArgs e) => await Guard(() => { var path = SavePath("glossary", "txt", "Export glossary"); if (path != null) Exporter.Save(path, ContextBox.Text); return Task.CompletedTask; });
    private void FilterModels(object sender, RoutedEventArgs e) { if (!ready) return; var id = (ModelsGrid.SelectedItem as Model)?.Id; var items = models.Catalogue.Where(m => m.Name.Contains(ModelSearch.Text, StringComparison.OrdinalIgnoreCase) && (ModelRoleBox.SelectedIndex == 0 || m.Role == (ModelRoleBox.SelectedIndex == 1 ? "asr" : "llm"))).ToList(); ModelsGrid.ItemsSource = items; ModelsGrid.SelectedItem = items.FirstOrDefault(m => m.Id == id) ?? items.FirstOrDefault(); }
    private void ModelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || ModelsGrid.SelectedItem is not Model model) return;
        ModelDetails.Text = $"{model.Name} · {model.InstalledStatus}\n{model.Description}\n{Hardware.Rating(model)} · estimated runtime RAM {model.EstimatedMemoryMb:N0} MB\nLanguages: {(model.Languages.Length == 0 ? "Unverified" : string.Join(", ", model.Languages))}\nLicense: {model.License} · Revision: {model.Revision}\n{(model.Available ? "Storage: " + models.Location(model) : model.Blocker)}";
    }
    private async void DownloadModel(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var model = ModelsGrid.SelectedItem as Model ?? throw new IOException("Select a model."); if (!model.Available) throw new IOException(model.Blocker);
        var token = BeginOperation();
        try { Note("Downloading " + model.Name); await models.Download(model, (done, total) => Dispatcher.BeginInvoke(() => { DownloadProgress.Value = total == 0 ? 0 : done * 100d / total; Note($"Downloading {model.Name}: {done / 1048576d:N1} / {total / 1048576d:N1} MB"); }), token); Note("Model verified and installed: " + model.Name); } finally { EndOperation(); }
    });
    private async void ImportModel(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        RequireIdle(); var profile = ModelsGrid.SelectedItem as Model ?? throw new IOException("Select the compatible model profile first."); if (!profile.Available) throw new IOException(profile.Blocker);
        string? path;
        if (profile.Runtime == "onnx-asr") path = PickFolder("Choose Parakeet V3 ONNX directory with config, vocab, INT8 encoder and decoder");
        else { var dialog = new OpenFileDialog { Title = "Import compatible " + profile.Runtime + " weights", Filter = profile.Runtime == "llama.cpp" ? "GGUF text model|*.gguf" : "whisper.cpp model|*.bin" }; path = dialog.ShowDialog(this) == true ? dialog.FileName : null; }
        if (path == null) return; var token = BeginOperation(); await pipeline.InferenceGate.WaitAsync(token);
        try { await models.Import(path, profile, async (model, dir, cancellation) =>
        {
            Note("Validating imported model in an isolated CPU worker");
            if (model.Runtime == "llama.cpp") { await using var server = await LocalLlm.Start(processes, Directory.GetFiles(dir, "*.gguf").Single(), new SummarySettings { Threads = store.Settings.Threads }, cancellation); await server.Generate(new[] { new { role = "user", content = "Reply with the word Ready." } }, new SummarySettings { OutputTokens = 64 }, [], cancellation); }
            else if (model.Runtime == "onnx-asr") { var input = Path.Combine(store.Settings.CacheRoot, "inspect-" + Guid.NewGuid().ToString("N") + ".json"); try { JsonFiles.Write(input, new { runtime = "onnx-asr", operation = "inspect", modelPath = dir, threads = store.Settings.Threads }); await processes.Run(processes.Tool("python", "python.exe"), [Path.Combine(AppContext.BaseDirectory, "workers/asr_worker.py"), input], cancellation); } finally { File.Delete(input); } }
            else { var silence = Path.Combine(store.Settings.CacheRoot, "inspect-silence.wav"); await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "1", "-y", silence], cancellation); try { await processes.Run(processes.Tool("whisper", "whisper-cli.exe"), ["--no-gpu", "-m", Directory.GetFiles(dir, "*.bin").Single(), "-f", silence, "-t", "2", "-l", "auto"], cancellation); } finally { File.Delete(silence); } }
        }, token); Note("Imported model passed a CPU load/inference check. Verify its licence before sharing it."); }
        finally { pipeline.InferenceGate.Release(); EndOperation(); }
    });
    private async void DeleteModel(object sender, RoutedEventArgs e) => await Guard(async () => { RequireIdle(); var model = ModelsGrid.SelectedItem as Model ?? throw new IOException("Select a model."); if (MessageBox.Show(this, $"Remove only {model.Name} from model storage?\n\nTranscripts, summaries and source files remain intact.", "Remove model", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; var bytes = await models.Delete(model, CancellationToken.None); RefreshModels(); Note($"Model removed; {bytes / 1048576d:N0} MB reclaimed."); });
    private async void OpenModelLocation(object sender, RoutedEventArgs e) => await Guard(() => { var model = ModelsGrid.SelectedItem as Model ?? throw new IOException("Select a model."); Open(models.Location(model)); return Task.CompletedTask; });
    private async void OpenModelCard(object sender, RoutedEventArgs e) => await Guard(() => { var model = ModelsGrid.SelectedItem as Model ?? throw new IOException("Select a model."); if (!model.Source.StartsWith("https://huggingface.co/", StringComparison.Ordinal)) throw new IOException("This imported model has no verified remote model card."); Process.Start(new ProcessStartInfo(model.Source) { UseShellExecute = true }); return Task.CompletedTask; });
    private void CancelOperation(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void ShowDevice()
    {
        var (total, free) = Hardware.Memory(); var drive = new DriveInfo(Path.GetPathRoot(store.Settings.ModelRoot)!);
        string Disk(string path) { try { return new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace / 1073741824d + " GB free"; } catch (IOException) { return "Unavailable"; } }
        DeviceInfo.Text = $"{Hardware.Cpu}\nWindows {Environment.OSVersion.Version} · {Hardware.PhysicalCores()?.ToString() ?? "Unknown"} physical / {Environment.ProcessorCount} logical cores · AVX2 {(Hardware.Compatible ? "supported" : "NOT supported — this runtime cannot run inference")}\nRAM: {total / 1073741824d:N1} GB total · {free / 1073741824d:N1} GB currently available\nModel drive: {drive.AvailableFreeSpace / 1073741824d:N1} GB free · Cache: {Disk(store.Settings.CacheRoot)}\nWorkspace: {(workspace == null ? "Not selected" : Disk(workspace.Path))}\n{(total < 16L * 1073741824 ? "Less than 16 GB RAM: use lightweight models and conservative context." : "Memory headroom is assessed separately for each model.")}\nDetected display drivers: {string.Join(", ", Hardware.Gpus())}. GPU acceleration is disabled.";
    }
    private void RefreshDevice(object sender, RoutedEventArgs e) { if (ready) ShowDevice(); }
    private void ApplyTheme()
    {
        Application.Current.ThemeMode = store.Settings.Theme switch { "Dark" => ThemeMode.Dark, "Light" => ThemeMode.Light, _ => ThemeMode.System };
        // Load this window's current palette before deriving its custom styles.
        var suffix = store.Settings.Theme is "Light" or "Dark" ? "." + store.Settings.Theme : "";
        var fluent = new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent" + suffix + ".xaml") };
        Resources.MergedDictionaries.Clear(); Resources.MergedDictionaries.Add(fluent);
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(TextBlock), typeof(DataGrid), typeof(TabItem) })
            if (Resources[type] is Style previous && fluent[type] is Style inherited) { var style = new Style(type, inherited); foreach (var setter in previous.Setters) style.Setters.Add(setter); Resources[type] = style; }
    }
    private void ThemeChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; store.Settings.Theme = Choice(ThemeBox); ApplyTheme(); store.SaveSettings(); }
    private async void SaveSettings(object sender, RoutedEventArgs e) => await Guard(() => { store.Settings.Threads = Number(ThreadsBox, 1, 64); store.SaveSettings(); Note("Settings saved."); return Task.CompletedTask; });
    private async void MoveModels(object sender, RoutedEventArgs e) => await Guard(async () => { RequireIdle(); var target = PickFolder("Choose an empty model storage folder"); if (target == null) return; var token = BeginOperation(); try { var previous = store.Settings.ModelRoot; await models.MoveStorage(target, (done, total) => Dispatcher.BeginInvoke(() => Note($"Moving and verifying models: {done / 1048576:N0} / {total / 1048576:N0} MB")), token); Note("Model storage moved and verified. The old copy remains at " + previous + "; remove it manually after checking the new installation."); } finally { EndOperation(); } });
    private async void ChangeCache(object sender, RoutedEventArgs e) => await Guard(() => { RequireIdle(); var target = PickFolder("Choose a disposable cache folder"); if (target != null) { if (Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Choose an empty dedicated cache folder."); ModelManager.OwnedPath(target, "validation"); store.Settings.CacheRoot = target; store.SaveSettings(); RefreshModels(); } return Task.CompletedTask; });
    private async void CleanCache(object sender, RoutedEventArgs e) => await Guard(() => { RequireIdle(); if (MessageBox.Show(this, "Delete only this app's disposable playback previews and inspection files from the cache?\n\nModels and saved results are kept.", "Clean disposable cache", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return Task.CompletedTask; Player.Close(); foreach (var file in Directory.GetFiles(store.Settings.CacheRoot).Where(f => Path.GetFileName(f).StartsWith("preview-") || Path.GetFileName(f).StartsWith("playback-") || Path.GetFileName(f).StartsWith("inspect-"))) { ModelManager.OwnedPath(store.Settings.CacheRoot, Path.GetFileName(file)); File.Delete(file); } Note("Disposable cache cleaned."); return Task.CompletedTask; });
    private async void OpenAppData(object sender, RoutedEventArgs e) => await Guard(() => { Open(store.Root); return Task.CompletedTask; });
    private void ShowDocument(string title, string relative) { var dialog = new Window { Owner = this, Title = title, Width = 860, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner }; dialog.Content = new TextBox { Text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relative)), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) }; dialog.Show(); }
    private async void OpenUserGuide(object sender, RoutedEventArgs e) => await Guard(() => { ShowDocument("Local Transcriber user guide", "docs/USER_GUIDE.md"); return Task.CompletedTask; });
    private async void OpenNotices(object sender, RoutedEventArgs e) => await Guard(() => { ShowDocument("Third-party notices", "THIRD_PARTY_NOTICES.md"); return Task.CompletedTask; });
    private void Navigated(object sender, SelectionChangedEventArgs e) { if (ready && ReferenceEquals(e.Source, Navigation) && SettingsPage.IsSelected) ShowDevice(); }
    private void ClosingApp(object? sender, CancelEventArgs e)
    {
        if (pipeline == null) { processes.Dispose(); return; }
        var testing = Environment.GetCommandLineArgs().Contains("--ui-check");
        if (testing) { CancelDictation(this, new RoutedEventArgs()); if (DictationTarget.ClipboardNeedsRestore && !DictationTarget.ClipboardWasChanged) DictationTarget.RestoreClipboard(); dirty = summaryDirty = false; }
        if (!AllowDiscard()) { e.Cancel = true; return; }
        if (!testing && ready) { try { CommitSpeechPrompts(); store.SaveSettings(); } catch (Exception error) { if (MessageBox.Show(this, "The prompt has unsaved invalid changes. Close without saving them?\n\n" + error.Message, "Speech prompt", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; SpeechPage.IsSelected = true; SpeechPromptEditor.IsExpanded = true; return; } } }
        if (!testing && DictationTarget.ClipboardNeedsRestore && MessageBox.Show(this, "A previous clipboard is waiting to be restored in Speech-to-text. Close without restoring it?", "Clipboard recovery", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; SpeechPage.IsSelected = true; RestoreClipboardButton.BringIntoView(); return; }
        if (!testing && (microphone != null || pipeline.Busy || operation != null) && MessageBox.Show(this, "Close and stop recording or processing? Recorded audio and valid transcription checkpoints are kept.", "Close Local Transcriber", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        CancelDictation(this, new RoutedEventArgs()); widget?.Close();
        ready = false; playback.Stop(); Player.Close(); operation?.Cancel(); pipeline.Dispose(); processes.Dispose();
    }
    private string? TextPrompt(string title, string initial)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 420, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) }; var input = new TextBox { Text = initial, MaxLength = 160 }; var button = new Button { Content = "Save", IsDefault = true }; button.Click += (_, _) => dialog.DialogResult = true; panel.Children.Add(input); panel.Children.Add(button); dialog.Content = panel; return dialog.ShowDialog() == true ? input.Text : null;
    }
}
