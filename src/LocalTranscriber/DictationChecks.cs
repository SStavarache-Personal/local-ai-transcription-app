using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;

namespace LocalTranscriber;
public static class DictationChecks
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    public static uint ForegroundProcess() { GetWindowThreadProcessId(GetForegroundWindow(), out var process); return process; }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    public static void Click(Button button) { var point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2)); SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
    public static void ShowInputHost(string[] args)
    {
        var output = args[Array.IndexOf(args, "--input-test-host") + 1];
        var textbox = new TextBox { Text = "before SELECT after", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20), FontSize = 20 };
        var window = new Window { Title = "Local Transcriber insertion test field", Width = 580, Height = 260, Left = 20, Top = 20, Content = textbox };
        var blockPaste = false;
        CommandManager.AddPreviewExecutedHandler(textbox, (_, e) => { if (blockPaste && e.Command == ApplicationCommands.Paste) e.Handled = true; });
        Application.Current.MainWindow = window;
        window.Loaded += (_, _) => { window.Activate(); textbox.Focus(); textbox.Select(7, 6); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => {
            var command = output + ".command";
            if (File.Exists(command)) { var value = File.ReadAllText(command); File.Delete(command); if (value == "block-paste") blockPaste = true; else if (value == "allow-paste") blockPaste = false; else if (value == "readonly") textbox.IsReadOnly = true; else if (value == "password") { var password = new PasswordBox { Margin = new Thickness(20) }; window.Content = password; password.Focus(); } }
            JsonFiles.Write(output, new { Text = textbox.Text, ProcessId = Environment.ProcessId, Handle = new WindowInteropHelper(window).Handle.ToInt64() });
        };
        timer.Start(); window.Show();
    }
    public static void FocusHost(long handle) => SetForegroundWindow((IntPtr)handle);
}

public partial class MainWindow
{
    public async Task CheckDictation(string[] args)
    {
        string Arg(string name) => args[Array.IndexOf(args, name) + 1];
        var output = Arg("--output"); Directory.CreateDirectory(output);
        store.Settings.ModelRoot = Arg("--models"); store.SaveSettings(); RefreshModels();
        changingSpeechPrompt = true; speechPrompts = new(SpeechPrompts.Defaults()); SpeechPromptBox.ItemsSource = speechPrompts; SpeechPromptBox.SelectedItem = speechPrompts[0]; changingSpeechPrompt = false; LoadSpeechPrompt(); CommitSpeechPrompts(); store.SaveSettings();
        if (args.Contains("--prompt-editor-only"))
        {
            AddSpeechPrompt(this, new RoutedEventArgs()); var added = SelectedSpeechPrompt(); SpeechPromptName.Text = "Renamed prompt"; SpeechPromptInstructions.Text = "Output only custom text."; SaveSpeechPrompt(this, new RoutedEventArgs());
            SpeechPromptBox.SelectedItem = speechPrompts.Single(p => p.Id == "document");
            if (SelectedSpeechPrompt().Id != "document" || SpeechPromptInstructions.Text != SpeechPrompts.DocumentInstructions) throw new Exception("Prompt selection after rename failed: " + DictationStatus.Text);
            SpeechPromptBox.SelectedItem = added; if (SpeechPromptInstructions.Text != "Output only custom text.") throw new Exception("Custom prompt edits lost on switching.");
            ResetSpeechPrompt(this, new RoutedEventArgs()); if (SpeechPromptInstructions.Text != SpeechPrompts.LightInstructions) throw new Exception("Prompt reset failed.");
            DeleteSpeechPrompt(this, new RoutedEventArgs()); if (speechPrompts.Count != 2) throw new Exception("Prompt deletion failed.");
            JsonFiles.Write(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT")!, new { Check = "Prompt editor selection/rename/reset/delete", Result = "Passed" }); return;
        }
        var results = new List<object>();
        if (!args.Contains("--field-only"))
        {
        await models.Download(models.Catalogue.Single(m => m.Id == "qwen25-7b-instruct"), (_, _) => { }, CancellationToken.None); RefreshModels();
        ShowTutorial(this, new RoutedEventArgs());
        if (!HelpPage.IsSelected || TutorialPanel.Visibility != Visibility.Visible) throw new Exception("Getting started tutorial was not shown.");
        FinishTutorial(this, new RoutedEventArgs());
        if (store.Settings.TutorialVersion != 1 || TutorialPanel.Visibility != Visibility.Collapsed || !SpeechPage.IsSelected) throw new Exception("Tutorial completion was not persisted.");
        ShowTutorial(this, new RoutedEventArgs()); if (TutorialPanel.Visibility != Visibility.Visible) throw new Exception("Tutorial cannot reopen."); FinishTutorial(this, new RoutedEventArgs());
        var settings = JsonFiles.Read<Settings>(Path.Combine(store.Root, "settings.json")); if (settings.TutorialVersion != 1) throw new Exception("Saved tutorial state incorrect.");
        if (Navigation.Items.Count != 8 || ((TabItem)Navigation.Items[6]) != HelpPage || ((TabItem)Navigation.Items[7]) != SettingsPage || SpeechPage.ActualWidth < 170) throw new Exception("Sidebar order or width incorrect.");
        DictationModelBox.SelectedItem = models.Catalogue.Single(m => m.Id == "parakeet-v3-int8");
        DictationLlmBox.SelectedItem = models.Catalogue.Single(m => m.Id == "qwen25-7b-instruct");
        vocabularyRows.Clear(); vocabularyRows.Add(new() { Heard = "Friday", Replacement = "vendredi" }); SaveDictationPreferences();
        recordingModel = (Model)DictationModelBox.SelectedItem; recordingLlm = (Model)DictationLlmBox.SelectedItem; recordingLanguage = "auto"; recordingRewrite = false; recordingVocabulary = VocabularyMap.Validate(vocabularyRows);
        var watch = Stopwatch.StartNew(); await RunDictation(Path.Combine(Arg("--fixtures"), "english.wav"), null); watch.Stop();
        if (dictationResult == null || !dictationResult.Raw.Contains("Friday", StringComparison.OrdinalIgnoreCase) || !dictationResult.Mapped.Contains("vendredi") || dictationResult.Text != dictationResult.Mapped || dictationResult.Rewritten) throw new Exception("Transcription-only workbench/vocabulary failed: " + DictationStatus.Text);
        var rawPath = Path.Combine(dictationResult.Directory, "transcript.raw.json"); var rawHash = await ModelManager.Hash(rawPath, CancellationToken.None);
        var edited = JsonFiles.Read<Transcript>(Path.Combine(dictationResult.Directory, "transcript.edited.json")); if (!edited.FullText.Contains("vendredi")) throw new Exception("File export vocabulary pipeline was not applied.");
        results.Add(new { Check = "Real Parakeet ASR, vocabulary, transcription-only workbench", WallSeconds = watch.Elapsed.TotalSeconds, Result = dictationResult });
        if (dictationResult.Raw.Count(c => c == '\n') < 1 || dictationResult.Text.Contains('\n') || dictationResult.AsrSeconds is not > 0 || dictationResult.LlmSeconds != null || !SpeechTiming.Text.Contains("LLM: off")) throw new Exception("Segment normalization or ASR-only timings failed.");
        DictationModelBox.SelectedItem = models.Catalogue.Single(m => m.Id == "whisper-small");
        DictationLanguageBox.SelectedItem = "en"; DictationRewrite.IsChecked = true; SaveDictationPreferences();
        recordingModel = (Model)DictationModelBox.SelectedItem; recordingLanguage = "en"; recordingRewrite = true;
        await RunDictation(Path.Combine(Arg("--fixtures"), "english.wav"), null);
        if (dictationResult?.Rewritten != true || !dictationResult.Text.Contains("vendredi", StringComparison.OrdinalIgnoreCase)) throw new Exception("Whisper + optional cleanup failed: " + DictationStatus.Text);
        var selectedJob = JsonFiles.Read<Job>(Path.Combine(dictationResult.Directory, "job.json")); if (selectedJob.ModelId != "whisper-small") throw new Exception("Workbench model selection ignored.");
        if (await ModelManager.Hash(rawPath, CancellationToken.None) != rawHash) throw new Exception("New workbench test overwrote old raw output.");
        results.Add(new { Check = "Real selected Whisper model + Qwen cleanup", Result = dictationResult });
        if (dictationResult.AsrSeconds is not > 0 || dictationResult.LlmSeconds is not > 0 || !SpeechTiming.Text.Contains(" s")) throw new Exception("Separate speech/LLM elapsed times missing.");
        var engine = new Dictation(store, models, processes, pipeline);
        var speech = "Um, please update the MAA request tracker tomorrow, no, on Friday. Uh, keep the budget at two thousand dollars. And, um, ask Marie to review the French transcript.";
        var clean = await engine.Rewrite(speech, recordingLlm!, DictationNote, CancellationToken.None);
        if (!clean.Contains("Friday", StringComparison.OrdinalIgnoreCase) || !clean.Contains("MAA", StringComparison.OrdinalIgnoreCase) || !clean.Contains("Marie") || !clean.Contains("French", StringComparison.OrdinalIgnoreCase) || !(clean.Contains("two thousand", StringComparison.OrdinalIgnoreCase) || clean.Contains("2,000") || clean.Contains("2000")) || clean.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)) throw new Exception("Cleanup did not preserve sample meaning/self-correction: " + clean);
        results.Add(new { Check = "Actual Qwen filler and self-correction cleanup", Source = speech, Cleaned = clean });
        var revised = await engine.Rewrite("Please use the blue folder. Actually, use the green folder. Keep the file names unchanged.", recordingLlm!, DictationNote, CancellationToken.None);
        if (revised.Contains("blue", StringComparison.OrdinalIgnoreCase) || !revised.Contains("green", StringComparison.OrdinalIgnoreCase) || !revised.Contains("unchanged", StringComparison.OrdinalIgnoreCase)) throw new Exception("Cross-sentence self-correction failed: " + revised);
        results.Add(new { Check = "Cross-sentence self-correction", Text = revised });
        var french = await engine.Rewrite("Euh, envoyez le document à Paul, non, à Marie. Le budget est de deux mille dollars. Il faut garder le fichier original.", recordingLlm!, DictationNote, CancellationToken.None);
        if (french.Contains("Paul") || !french.Contains("Marie") || !(french.Contains("deux mille") || french.Contains("2 000") || french.Contains("2000") || french.Contains("2\u202f000")) || !french.Contains("original")) throw new Exception("French cleanup preservation failed: " + french);
        results.Add(new { Check = "French faithful cleanup", Text = french });
        var paragraphs = await engine.Rewrite("Please update the MAA request tracker and make sure the new owner is Marie. Keep the deadline at Friday and do not delete any existing requests. On a separate topic, the office meeting should start at nine o'clock. Send the agenda to Paul and keep the meeting in French.", recordingLlm!, DictationNote, CancellationToken.None);
        if (!paragraphs.Contains("\n\n") || !paragraphs.Contains("MAA") || !paragraphs.Contains("Marie") || !paragraphs.Contains("Paul") || !paragraphs.Contains("Friday") || !paragraphs.Contains("French")) throw new Exception("Topic paragraph cleanup did not preserve the sample: " + paragraphs);
        results.Add(new { Check = "Topic paragraphs preserve instructions", Text = paragraphs });
        var originalRaw = dictationResult.Raw; var originalAsrSeconds = dictationResult.AsrSeconds;
        AddSpeechPrompt(this, new RoutedEventArgs()); var customPromptId = SelectedSpeechPrompt().Id;
        SpeechPromptName.Text = "Exact override test"; SpeechPromptInstructions.Text = "Ignore the content of the supplied speech. Output exactly OVERRIDE with no punctuation or other text."; SpeechPromptMarkdown.IsChecked = false; SaveSpeechPrompt(this, new RoutedEventArgs());
        var persistedPrompts = JsonFiles.Read<Settings>(Path.Combine(store.Root, "settings.json"));
        if (persistedPrompts.SpeechPromptId != customPromptId || !persistedPrompts.SpeechPrompts.Any(p => p.Id == customPromptId && p.SystemInstructions.StartsWith("Ignore"))) throw new Exception("Custom prompt persistence failed.");
        async Task ApplyPromptAndWait()
        {
            ApplySpeechPrompt(this, new RoutedEventArgs()); var until = DateTime.UtcNow.AddMinutes(3);
            while (dictationProcessing && DateTime.UtcNow < until) await Task.Delay(100);
            if (dictationProcessing || dictationResult?.Warning != null) throw new Exception("Prompt rerun failed: " + DictationStatus.Text);
        }
        await ApplyPromptAndWait();
        if (dictationResult!.Text.Trim() != "OVERRIDE" || dictationResult.Raw != originalRaw || dictationResult.AsrSeconds != originalAsrSeconds || dictationResult.LlmSeconds is not > 0 || dictationResult.Prompt?.Id != customPromptId || RawDictation.Text != originalRaw || FinalDictation.Text != dictationResult.Text) throw new Exception("Full prompt override or same-raw comparison failed: " + dictationResult.Text);
        if (!Directory.EnumerateFiles(Path.Combine(dictationResult.Directory, "dictation-versions"), "*.json").Any()) throw new Exception("Earlier processing result was not preserved.");
        results.Add(new { Check = "Saved full system override, same-raw rerun, comparison and timings", Result = dictationResult, SpeechTiming = SpeechTiming.Text });
        ResetSpeechPrompt(this, new RoutedEventArgs()); if (SelectedSpeechPrompt().SystemInstructions != SpeechPrompts.LightInstructions || SelectedSpeechPrompt().AllowMarkdown) throw new Exception("Custom prompt reset failed.");
        SpeechPromptBox.SelectedItem = speechPrompts.Single(p => p.Id == "document"); ResetSpeechPrompt(this, new RoutedEventArgs());
        if (SelectedSpeechPrompt().Id != "document") throw new Exception("Document prompt selection failed: " + DictationStatus.Text);
        await ApplyPromptAndWait();
        if (dictationResult!.Prompt?.AllowMarkdown != true || !dictationResult.Text.Contains('#') || !dictationResult.Text.Contains("Marie") || dictationResult.Raw != originalRaw) throw new Exception("Document/Markdown prompt failed: " + DictationStatus.Text + " · " + dictationResult.Text);
        results.Add(new { Check = "Document prompt with Markdown", Text = dictationResult.Text });
        SpeechPromptMarkdown.IsChecked = false; SaveSpeechPrompt(this, new RoutedEventArgs()); await ApplyPromptAndWait();
        if (dictationResult!.Prompt?.AllowMarkdown != false || dictationResult.Text.Contains("# ") || dictationResult.Text.Contains("**") || dictationResult.Text.Contains('`') || !dictationResult.Text.Contains("Marie")) throw new Exception("Plain-text document output failed: " + dictationResult.Text);
        results.Add(new { Check = "Same document prompt with plain-text policy", Text = dictationResult.Text });
        SpeechPromptBox.SelectedItem = speechPrompts.Single(p => p.Id == customPromptId); DeleteSpeechPrompt(this, new RoutedEventArgs());
        SpeechPromptBox.SelectedItem = speechPrompts.Single(p => p.Id == "document"); ResetSpeechPrompt(this, new RoutedEventArgs()); SpeechPromptBox.SelectedItem = speechPrompts[0]; SaveSpeechPrompt(this, new RoutedEventArgs());
        if (JsonFiles.Read<Settings>(Path.Combine(store.Root, "settings.json")).SpeechPrompts.Any(p => p.Id == customPromptId)) throw new Exception("Deleted custom prompt persisted.");
        var formattingSample = string.Join("\n\n", Enumerable.Repeat("# Note\n\n**Marie** reviews the `MAA` tracker on Friday.", 100));
        var formatting = Stopwatch.StartNew(); for (var repeat = 0; repeat < 100; repeat++) DictationText.Format(formattingSample, false); formatting.Stop();
        results.Add(new { Check = "Local Markdown conversion cost", InputCharacters = formattingSample.Length, AverageMilliseconds = formatting.Elapsed.TotalMilliseconds / 100 });
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); try { await engine.Transcribe(Path.Combine(Arg("--fixtures"), "english.wav"), recordingModel, "en", false, null, [], _ => { }, canceled.Token); throw new Exception("Canceled transcription unexpectedly finished."); } catch (OperationCanceledException) { }
            if (store.Jobs().Any(j => j.State == JobState.Queued)) throw new Exception("Canceled dictation left a queued job.");
        }
        JsonFiles.Write(Path.Combine(output, "processing-results.json"), new { Check = "Real speech processing, prompts, comparison and timing", Result = "Passed", Tests = results });
        }
        DictationRewrite.IsChecked = false; ShowWidget(this, new RoutedEventArgs()); if (widget == null || !widget.IsVisible) throw new Exception("Widget did not open.");
        if (widget.TimingText.Text != SpeechTiming.Text || !widget.ModelName.Text.Contains(SelectedSpeechPrompt().Name)) throw new Exception("Widget prompt choice/timing display missing.");
        foreach (var theme in new[] { "Light", "Dark" })
        {
            store.Settings.Theme = theme; ApplyTheme(); SpeechPage.IsSelected = true; await Task.Delay(150); SaveUiImage(Path.Combine(output, theme.ToLowerInvariant() + "-speech.png"));
            RawDictation.BringIntoView(); await Task.Delay(100); SaveUiImage(Path.Combine(output, theme.ToLowerInvariant() + "-comparison.png"));
            SpeechPromptEditor.IsExpanded = true; SpeechPromptInstructions.BringIntoView(); await Task.Delay(100); SaveUiImage(Path.Combine(output, theme.ToLowerInvariant() + "-prompts.png")); SpeechPromptEditor.IsExpanded = false;
            SaveUiImage(Path.Combine(output, theme.ToLowerInvariant() + "-widget.png"), widget);
            HelpPage.IsSelected = true; ShowTutorial(this, new RoutedEventArgs()); await Task.Delay(150); SaveUiImage(Path.Combine(output, theme.ToLowerInvariant() + "-help.png"));
        }
        Width = 980; Height = 720; SpeechPage.IsSelected = true; await Task.Delay(150); SaveUiImage(Path.Combine(output, "minimum-speech.png")); Width = 1320; Height = 920;
        var hostReport = Path.Combine(output, "input-host.json"); if (File.Exists(hostReport)) File.Delete(hostReport);
        if (File.Exists(hostReport + ".command")) File.Delete(hostReport + ".command");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true }; start.ArgumentList.Add("--input-test-host"); start.ArgumentList.Add(hostReport);
        using var host = Process.Start(start)!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15); while (!File.Exists(hostReport) && DateTime.UtcNow < deadline) await Task.Delay(100);
            var hostState = JsonFiles.Read<System.Text.Json.JsonElement>(hostReport); DictationChecks.FocusHost(hostState.GetProperty("handle").GetInt64()); await Task.Delay(400);
            var target = DictationTarget.Capture(out var targetNote); if (target?.ProcessId != host.Id) throw new Exception("Test field did not receive focus: " + targetNote);
            widget.Show(); await Task.Delay(150);
            DictationChecks.Click(widget.RecordButton); await Task.Delay(400);
            if (microphone == null || dictationTarget?.ProcessId != host.Id) throw new Exception("Widget Record click lost the target or failed to capture: " + DictationStatus.Text);
            DictationChecks.Click(widget.CancelButton); await Task.Delay(150);
            if (microphone != null || DictationTarget.Capture(out targetNote)?.ProcessId != host.Id) throw new Exception("Widget Cancel click did not preserve focus/close recording.");
            var oldClipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            await target.Insert("Bonjour Québec.\nMAA request tracker."); await Task.Delay(300);
            var restoredClipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (restoredClipboardText != oldClipboardText) throw new Exception($"Automatic paste did not restore clipboard text (before/after characters {oldClipboardText?.Length}/{restoredClipboardText?.Length}, line-ending difference {restoredClipboardText?.Replace("\r\n", "\n") == oldClipboardText?.Replace("\r\n", "\n")}).");
            hostState = JsonFiles.Read<System.Text.Json.JsonElement>(hostReport);
            if (hostState.GetProperty("text").GetString()?.Replace("\r\n", "\n") != "before Bonjour Québec.\nMAA request tracker. after") throw new Exception("External selection insertion/Unicode/paragraphs failed: " + hostState.GetProperty("text"));
            var appendTarget = DictationTarget.Capture(out targetNote); if (appendTarget?.ProcessId != host.Id) throw new Exception("Test field lost focus before ASR insertion.");
            recordingModel = models.Catalogue.Single(m => m.Id == "parakeet-v3-int8"); recordingLanguage = "auto"; recordingRewrite = false;
            await RunDictation(Path.Combine(Arg("--fixtures"), "english.wav"), appendTarget); await Task.Delay(300);
            hostState = JsonFiles.Read<System.Text.Json.JsonElement>(hostReport);
            if (!DictationStatus.Text.StartsWith("Inserted") || !hostState.GetProperty("text").GetString()!.Replace("\r\n", "\n").Contains(dictationResult!.Text.Replace("\r\n", "\n"))) throw new Exception("ASR → vocabulary → external field insertion failed: " + DictationStatus.Text + $" (foreground PID {DictationChecks.ForegroundProcess()}, expected {host.Id}).");
            File.WriteAllText(hostReport + ".command", "block-paste"); await Task.Delay(250);
            var blockedTarget = DictationTarget.Capture(out targetNote); var blocked = false;
            try { await blockedTarget!.Insert("Blocked test\nparagraph."); } catch (IOException) { blocked = true; }
            if (!blocked || !DictationTarget.ClipboardNeedsRestore) throw new Exception("Unconfirmed paste did not preserve clipboard recovery.");
            DictationTarget.RestoreClipboard(); if ((Clipboard.ContainsText() ? Clipboard.GetText() : null) != oldClipboardText) throw new Exception("Clipboard recovery did not restore original text.");
            File.WriteAllText(hostReport + ".command", "allow-paste"); await Task.Delay(250);
            DictationChecks.FocusHost(hostState.GetProperty("handle").GetInt64()); await Task.Delay(200);
            var oneLine = DictationTarget.Capture(out targetNote); if (oneLine?.ProcessId != host.Id) throw new Exception("Single-line test field lost focus: " + targetNote);
            await oneLine.Insert("Unicode café single line."); await Task.Delay(150);
            if (!JsonFiles.Read<System.Text.Json.JsonElement>(hostReport).GetProperty("text").GetString()!.Contains("Unicode café single line.")) throw new Exception("Single-line paste insertion failed.");
            File.WriteAllText(hostReport + ".command", "readonly"); await Task.Delay(250);
            if (DictationTarget.Capture(out _) != null) throw new Exception("Read-only field accepted dictation.");
            File.WriteAllText(hostReport + ".command", "password"); await Task.Delay(250);
            if (DictationTarget.Capture(out _) != null) throw new Exception("Password field accepted dictation.");
            Activate(); await Task.Delay(200); var rejected = false; try { await target.Insert("MUST NOT APPEAR"); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Changed focus did not prevent insertion.");
            results.Add(new { Check = "Separate-process editable field insertion", SelectionReplacement = true, Unicode = true, Paragraphs = true, RealAsrPipelineInsertion = true, ChangedFocusRejected = true, WidgetRecordCancelClicksPreservedFocus = true, ClipboardRestored = true, BlockedPasteRecovery = true, ReadOnlyAndPasswordRejected = true });
        }
        finally { if (!host.HasExited) { host.Kill(); await host.WaitForExitAsync(); } }
        // Capture real device buffers without treating ambient sound as spoken-ASR accuracy evidence.
        object microphoneCheck;
        try
        {
            SpeechPage.IsSelected = true; StartDictation(false); if (microphone == null) throw new IOException(DictationStatus.Text);
            await Task.Delay(1600); var captured = microphone; var recordingPath = captured.Path; var seconds = captured.Seconds; var peak = captured.Peak;
            CancelDictation(this, new RoutedEventArgs()); var probed = await pipeline.Probe(recordingPath, store.Workspaces().First().Id, CancellationToken.None);
            if (seconds < .5 || probed.Duration < .5 || microphone != null || microphoneTimer.IsEnabled) throw new Exception("Live microphone recording/cancel lifecycle failed.");
            microphoneCheck = new { Result = "Passed", Devices = Microphone.Devices(), Seconds = seconds, PeakRms = peak, CancelClosedDevice = true, Source = recordingPath, Limitation = "Ambient audio only; human spoken dictation accuracy not measured." };
        }
        catch (IOException error) { microphoneCheck = new { Result = "Unavailable", Reason = error.Message }; }
        results.Add(new { Check = "Live microphone capture", Evidence = microphoneCheck });
        FinishTutorial(this, new RoutedEventArgs()); widget?.Close(); store.Settings.Theme = "System"; ApplyTheme();
        JsonFiles.Write(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT")!, new { Check = args.Contains("--field-only") ? "Dictation field and microphone checks" : "Dictation 1.2 end-to-end", Result = "Passed", Tests = results, TutorialFirstUseAndReplay = !args.Contains("--field-only"), SidebarPages = Navigation.Items.Count, SavedSettings = store.Settings, Limitation = "AI preservation assertions cover these examples, not arbitrary speech. Physical spoken microphone quality and third-party/elevated application compatibility remain unverified." });
        dirty = summaryDirty = false;
    }
}
