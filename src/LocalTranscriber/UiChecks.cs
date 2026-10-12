using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalTranscriber;
public partial class MainWindow
{
    private void SaveUiImage(string path, Window? window = null)
    {
        window ??= this; window.UpdateLayout();
        // Fluent uses translucent fills over the native backdrop; flatten that backdrop for PNG evidence.
        var dark = window.Foreground is SolidColorBrush text && text.Color.R > 128;
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) { var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight); context.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243)), null, bounds); context.DrawRectangle(new VisualBrush(window), null, bounds); }
        var render = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); render.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render)); using var file = File.Create(path); encoder.Save(file);
    }
    public async Task CheckUi(string[] args)
    {
        string Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new IOException("UI check requires " + name); }
        var output = Arg("--output"); Directory.CreateDirectory(output);
        var fixture = Arg("--fixtures");
        store.Settings.ModelRoot = Arg("--models"); store.SaveSettings(); RefreshModels();
        var folder = Path.Combine(output, "ui-workspace"); Directory.CreateDirectory(folder);
        var added = store.AddWorkspace(folder); RefreshWorkspaces(added.Id);
        await ImportFiles([Path.Combine(fixture, "english.mp4")], false);
        if (selectedMedia == null || selectedMedia.Tracks.Count == 0) throw new Exception("UI import did not display audio metadata.");
        foreach (var theme in new[] { "Light", "Dark" })
        {
            store.Settings.Theme = theme; ApplyTheme();
            for (var page = 0; page < Navigation.Items.Count; page++)
            {
                Navigation.SelectedIndex = page; await Task.Delay(100); UpdateLayout();
                if (ActualWidth < 900 || !IsLoaded || !IsVisible) throw new Exception("Desktop window is not visible and laid out.");
                SaveUiImage(Path.Combine(output, $"{theme.ToLowerInvariant()}-{page}.png"));
            }
        }
        Width = 980; Height = 720; ModelsPage.IsSelected = true; await Task.Delay(100); SaveUiImage(Path.Combine(output, "minimum-window.png")); Width = 1320; Height = 920; await Task.Delay(100);
        Navigation.SelectedIndex = 1;
        SpeechModelBox.SelectedItem = models.Catalogue.Single(m => m.Id == "parakeet-v3-int8");
        var job = MakeJob(RequireMedia()); var ticks = 0; var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; timer.Tick += (_, _) => ticks++; timer.Start();
        var limit = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < limit) { await Task.Delay(100); var state = store.Jobs(added.Id).Single(j => j.Id == job.Id); if (state.State == JobState.Completed) { job = state; break; } if (state.State == JobState.Failed) throw new Exception("UI transcription failed: " + state.Error); }
        timer.Stop();
        if (job.State != JobState.Completed || ticks < 2) throw new Exception("UI queue failed or the dispatcher did not remain responsive.");
        RefreshJobs(); JobsGrid.SelectedItem = ((IEnumerable<Job>)JobsGrid.ItemsSource).Single(j => j.Id == job.Id); OpenJob(this, new RoutedEventArgs()); await Task.Delay(100);
        if (transcript == null || transcript.Segments.Count == 0 || TranscriptGrid.Items.Count == 0) throw new Exception("UI result did not open timed segments.");
        var raw = await ModelManager.Hash(Path.Combine(job.Directory, "transcript.raw.json"), CancellationToken.None);
        var originalText = transcript.Segments[0].Text;
        var row = transcript.Segments[0]; TranscriptGrid.SelectedItem = row; TranscriptGrid.CurrentCell = new DataGridCellInfo(row, TranscriptGrid.Columns[3]);
        if (!TranscriptGrid.BeginEdit()) throw new Exception("The transcript cell did not enter edit mode."); UpdateLayout();
        if (TranscriptGrid.Columns[3].GetCellContent(row) is not TextBox cell) throw new Exception("No editable transcript control was created.");
        cell.Text = originalText + " Québec — saved UI test"; TranscriptGrid.CommitEdit(DataGridEditingUnit.Cell, true); TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true); await Task.Delay(100);
        var editedText = transcript.Segments[0].Text;
        if (editedText == originalText) throw new Exception("The native editor did not commit text.");
        UndoTranscript(this, new RoutedEventArgs()); if (transcript.Segments[0].Text != originalText) throw new Exception("Undo did not restore the prior text.");
        RedoTranscript(this, new RoutedEventArgs()); if (transcript.Segments[0].Text != editedText) throw new Exception("Redo did not restore the edited text.");
        SaveTranscript(this, new RoutedEventArgs());
        if (dirty || Store.LoadTranscript(job).Segments[0].Text != transcript.Segments[0].Text || await ModelManager.Hash(Path.Combine(job.Directory, "transcript.raw.json"), CancellationToken.None) != raw) throw new Exception("UI edits did not persist independently from raw.");
        RevertTranscript(this, new RoutedEventArgs()); if (transcript.Segments[0].Text != originalText) throw new Exception("Revert did not load raw speech."); UndoTranscript(this, new RoutedEventArgs()); SaveTranscript(this, new RoutedEventArgs());
        VersionBox.SelectedIndex = 1; if (transcript.Segments[0].Text != originalText || !TranscriptGrid.IsReadOnly) throw new Exception("Raw version is not separately accessible."); VersionBox.SelectedIndex = 0;
        TranscriptSearch.Text = "Friday"; if (TranscriptGrid.Items.Count != 1) throw new Exception("Transcript search failed."); TranscriptSearch.Text = "";
        var exports = Path.Combine(output, "ui-exports"); Directory.CreateDirectory(exports);
        foreach (var format in new[] { "txt", "md", "json", "csv", "srt", "vtt" }) Exporter.Save(Path.Combine(exports, "transcript." + format), Exporter.Transcript(transcript, format));
        SetMedia(selectedMedia!.Path); Player.Play(); playing = true; await Task.Delay(1500);
        var previewOpened = Player.NaturalDuration.HasTimeSpan; Player.Pause(); playing = false; TranscriptGrid.CurrentCell = new DataGridCellInfo(transcript.Segments[1], TranscriptGrid.Columns[3]); TranscriptGrid.SelectedItem = transcript.Segments[1]; SeekTranscriptClick(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)); await Task.Delay(800); var seekNearSegment = Math.Abs(Player.Position.TotalMilliseconds - transcript.Segments[1].StartMs) < 1000;
        if (!previewOpened || !seekNearSegment) throw new Exception($"Playback opened={previewOpened}; requested {transcript.Segments[1].StartMs} ms, reached {Player.Position.TotalMilliseconds} ms, selected={(TranscriptGrid.SelectedItem as Segment)?.SegmentId}.");
        var summaryModel = models.Catalogue.Single(m => m.Id == "lfm25-1.2b-instruct"); SummaryModelBox.SelectedItem = summaryModel;
        summary = await summarizer.Generate(job, Clone(transcript), summaryModel, new SummarySettings { Threads = 8, OutputTokens = 256 }, _ => { }, CancellationToken.None); SummaryEditor.Text = summary.Text;
        var generatedPath = Path.Combine(job.Directory, "summaries", summary.Id, "summary.generated.json"); var generatedHash = await ModelManager.Hash(generatedPath, CancellationToken.None);
        summary.Text += "\n\nHuman-edited UI test note."; SummaryEditor.Text = summary.Text; SaveSummary(this, new RoutedEventArgs());
        if (summaryDirty || !File.Exists(Path.Combine(job.Directory, "summaries", summary.Id, "summary.generated.json"))) throw new Exception("UI summary version preservation failed.");
        if (await ModelManager.Hash(generatedPath, CancellationToken.None) != generatedHash) throw new Exception("Editing replaced the generated summary original.");
        Exporter.Save(Path.Combine(exports, "summary.md"), SummaryEditor.Text);
        Navigation.SelectedIndex = 2; store.Settings.Theme = "System"; ApplyTheme(); await Task.Delay(150);
        SaveUiImage(Path.Combine(output, "completed-workflow.png"));
        var result = new { Check = "Packaged WPF workflow", Result = "Passed", ResponsiveDispatcherTicks = ticks, Pages = Navigation.Items.Count, Themes = new[] { "Light", "Dark", "System" }, PreviewOpened = previewOpened, SeekNearSegment = seekNearSegment, NativeCellEditingUndoRedo = true, RawEditedVersionSwitch = true, Search = true, RecordedSegments = transcript.Segments.Count, TranscriptExports = 6, SummaryExport = true, Note = "Internal UI event/visual-tree check; file picker, Narrator and external input automation are not exercised. PNGs composite translucent native fills over a representative opaque backdrop." };
        File.WriteAllText(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT") ?? Path.Combine(output, "ui-results.json"), JsonSerializer.Serialize(result, JsonFiles.Options)); dirty = summaryDirty = false;
    }
}
