using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalTranscriber;
public partial class MainWindow
{
    private DictationWidget? widget;
    private Microphone? microphone;
    private readonly DispatcherTimer microphoneTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private ObservableCollection<VocabularyRule> vocabularyRows = [];
    private ObservableCollection<SpeechPrompt> speechPrompts = [];
    private bool changingSpeechPrompt;
    private SpeechPrompt recordingPrompt = SpeechPrompts.Default();
    private DictationTarget? dictationTarget;
    private DictationResult? dictationResult;
    private Model? recordingModel, recordingLlm;
    private string recordingLanguage = "auto";
    private bool recordingRewrite;
    private List<VocabularyRule> recordingVocabulary = [];
    private bool dictationProcessing;

    private void InitializeDictation()
    {
        vocabularyRows = new(store.Settings.Vocabulary.Select(r => r with { })); VocabularyGrid.ItemsSource = vocabularyRows;
        VocabularyToggle.IsChecked = store.Settings.VocabularyEnabled; DictationRewrite.IsChecked = store.Settings.RewriteDictation;
        speechPrompts = new(SpeechPrompts.Validate(store.Settings.SpeechPrompts));
        changingSpeechPrompt = true; SpeechPromptBox.ItemsSource = speechPrompts; SpeechPromptBox.SelectedItem = speechPrompts.FirstOrDefault(p => p.Id == store.Settings.SpeechPromptId) ?? speechPrompts[0]; changingSpeechPrompt = false; LoadSpeechPrompt();
        RefreshMicrophones(this, new RoutedEventArgs()); RefreshDictationModels();
        var latest = Path.Combine(store.Root, "dictation", "latest.json");
        if (File.Exists(latest)) { dictationResult = JsonFiles.Read<DictationResult>(latest); ShowDictationResult(); }
        microphoneTimer.Tick += (_, _) => {
            try { microphone?.Poll(); if (microphone != null) { var diameter = 22 + Math.Clamp(Math.Sqrt(microphone.Level) * 160, 0, 70); WorkbenchCircle.Width = WorkbenchCircle.Height = diameter; RecordingTime.Text = TimeSpan.FromSeconds(microphone.Seconds).ToString(@"hh\:mm\:ss"); System.Windows.Automation.AutomationProperties.SetHelpText(WorkbenchCircle, $"Microphone level {microphone.Level * 100:N1} percent"); widget?.SetLevel(microphone.Level, microphone.Seconds); } }
            catch (Exception error) { CancelDictation(this, new RoutedEventArgs()); DictationNote(error.Message); }
        };
        Loaded += (_, _) => {
            if (store.Settings.TutorialVersion < 1 && !Environment.GetCommandLineArgs().Contains("--ui-check")) ShowTutorial(this, new RoutedEventArgs());
            var launchReport = Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_STARTUP_REPORT");
            if (!string.IsNullOrWhiteSpace(launchReport)) JsonFiles.Write(launchReport, new { Version = "1.2.1", Visible = IsVisible, Title, Pages = Navigation.Items.Count, TutorialVisible = TutorialPanel.Visibility == Visibility.Visible, HelpSelected = HelpPage.IsSelected, InstalledModels = models.Catalogue.Where(models.Installed).Select(m => m.Id).ToArray(), DownloadableModels = models.Catalogue.Where(m => m.Available).Select(m => m.Id).ToArray(), SpeechPrompts = speechPrompts.Select(p => p.Name).ToArray() });
        };
    }
    private void RefreshDictationModels()
    {
        if (!ready) return;
        var asr = models.Catalogue.Where(m => m.Role == "asr" && m.Available && models.Installed(m)).ToList();
        var llm = models.Catalogue.Where(m => m.Role == "llm" && m.Available && models.Installed(m)).ToList();
        var selected = (DictationModelBox.SelectedItem as Model)?.Id ?? store.Settings.DictationModel;
        DictationModelBox.ItemsSource = asr; DictationModelBox.SelectedItem = asr.FirstOrDefault(m => m.Id == selected) ?? asr.FirstOrDefault();
        selected = (DictationLlmBox.SelectedItem as Model)?.Id ?? store.Settings.RewriteModel;
        DictationLlmBox.ItemsSource = llm; DictationLlmBox.SelectedItem = llm.FirstOrDefault(m => m.Id == selected) ?? llm.FirstOrDefault();
        DictationCleanupChanged(this, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        DictationModelChanged(this, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        RefreshWidgetLabel();
    }
    private void DictationModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        var model = DictationModelBox.SelectedItem as Model; var selected = DictationLanguageBox.SelectedItem as string ?? store.Settings.DictationLanguage;
        DictationLanguageBox.ItemsSource = model?.ForcedLanguage == true ? new[] { "auto" }.Concat(model.Languages).Distinct().ToList() : ["auto"];
        DictationLanguageBox.SelectedItem = DictationLanguageBox.Items.Cast<string>().Contains(selected) ? selected : "auto";
        DictationCapability.Text = model == null ? "Install a speech model in Models to begin." : model.Description + "\n" + Hardware.Rating(model);
    }
    private void RefreshMicrophones(object sender, RoutedEventArgs e)
    {
        if (microphone != null) return;
        var selected = (MicrophoneBox.SelectedItem as MicrophoneDevice)?.Id ?? store.Settings.MicrophoneId;
        MicrophoneBox.ItemsSource = Microphone.Devices(); MicrophoneBox.SelectedItem = MicrophoneBox.Items.Cast<MicrophoneDevice>().FirstOrDefault(d => d.Id == selected) ?? MicrophoneBox.Items[0];
    }
    private void DictationCleanupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        var model = DictationLlmBox.SelectedItem as Model;
        CleanupCapability.Text = model == null ? "Choose a language model for optional processing." : "Local CPU processing · " + Hardware.Rating(model) + "\nThe selected prompt controls the style and output format.";
    }
    private void SaveDictationPreferences()
    {
        CommitSpeechPrompts();
        VocabularyGrid.CommitEdit(DataGridEditingUnit.Cell, true); VocabularyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var rules = VocabularyMap.Validate(vocabularyRows);
        store.Settings.Vocabulary = rules; store.Settings.VocabularyEnabled = VocabularyToggle.IsChecked == true;
        store.Settings.DictationModel = (DictationModelBox.SelectedItem as Model)?.Id ?? store.Settings.DictationModel;
        store.Settings.RewriteModel = (DictationLlmBox.SelectedItem as Model)?.Id ?? store.Settings.RewriteModel;
        store.Settings.DictationLanguage = DictationLanguageBox.SelectedItem as string ?? "auto";
        store.Settings.MicrophoneId = (MicrophoneBox.SelectedItem as MicrophoneDevice)?.Id ?? -1;
        store.Settings.RewriteDictation = DictationRewrite.IsChecked == true; store.SaveSettings();
    }
    private void SaveSpeechSettings(object sender, RoutedEventArgs e) { try { SaveDictationPreferences(); DictationNote("Speech settings and vocabulary saved. Rules also apply to new file transcriptions."); } catch (Exception error) { DictationNote(error.Message); } }
    private void AddVocabulary(object sender, RoutedEventArgs e) { vocabularyRows.Add(new()); VocabularyGrid.SelectedItem = vocabularyRows.Last(); VocabularyGrid.ScrollIntoView(vocabularyRows.Last()); }
    private void RemoveVocabulary(object sender, RoutedEventArgs e) { if (VocabularyGrid.SelectedItem is VocabularyRule rule) vocabularyRows.Remove(rule); }
    private void DictationNote(string text) { DictationStatus.Text = text; if (widget != null) widget.StatusText.Text = text; }
    private void UpdateDictationState()
    {
        var recording = microphone != null; var busy = recording || dictationProcessing;
        SpeechOptions.IsEnabled = !busy; VocabularyPanel.IsEnabled = !busy; SpeechPromptOptions.IsEnabled = !busy;
        ApplySpeechPromptButton.IsEnabled = !busy && dictationResult != null;
        WorkbenchRecord.IsEnabled = !busy; WorkbenchStop.IsEnabled = recording; WorkbenchCancel.IsEnabled = busy; TestSpeechFile.IsEnabled = !busy;
        ShowWidgetButton.IsEnabled = !busy; widget?.SetState(recording, dictationProcessing, dictationResult != null);
        RestoreClipboardButton.Visibility = DictationTarget.ClipboardNeedsRestore ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShowWidget(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveDictationPreferences();
            if (widget == null)
            {
                widget = new DictationWidget { Left = SystemParameters.WorkArea.Right - 405, Top = SystemParameters.WorkArea.Bottom - 520 };
                widget.RecordButton.Click += (_, _) => StartDictation(true); widget.StopButton.Click += StopDictation;
                widget.CancelButton.Click += CancelDictation; widget.CopyButton.Click += CopyDictation;
                widget.WorkbenchButton.Click += (_, _) => { Activate(); SpeechPage.IsSelected = true; };
                widget.RewriteToggle.Click += (_, _) => { DictationRewrite.IsChecked = widget.RewriteToggle.IsChecked; SaveSpeechSettings(this, new RoutedEventArgs()); };
                widget.Closed += (_, _) => { CancelDictation(this, new RoutedEventArgs()); widget = null; };
            }
            widget.RewriteToggle.IsChecked = DictationRewrite.IsChecked; RefreshWidgetLabel();
            if (dictationResult != null) widget.TimingText.Text = DictationTiming(dictationResult);
            widget.Show(); UpdateDictationState();
            DictationNote("Select a text box in another app, then click Record in the widget. Keep that field focused until the result arrives.");
        }
        catch (Exception error) { DictationNote(error.Message); }
    }
    private void RecordWorkbench(object sender, RoutedEventArgs e) => StartDictation(false);
    private void StartDictation(bool insert)
    {
        try
        {
            RequireIdle(); SaveDictationPreferences();
            recordingModel = DictationModelBox.SelectedItem as Model ?? throw new IOException("Install and select a speech model first.");
            recordingLlm = DictationLlmBox.SelectedItem as Model; recordingRewrite = DictationRewrite.IsChecked == true;
            if (recordingRewrite && recordingLlm == null) throw new IOException("Select a language model for cleanup, or switch cleanup off.");
            recordingLanguage = store.Settings.DictationLanguage; recordingVocabulary = store.Settings.VocabularyEnabled ? VocabularyMap.Validate(store.Settings.Vocabulary) : [];
            recordingPrompt = SelectedSpeechPrompt() with { };
            dictationTarget = null; var targetMessage = "Workbench recording · result will appear here";
            if (insert) { dictationTarget = DictationTarget.Capture(out targetMessage); if (dictationTarget == null) throw new IOException(targetMessage); }
            var path = Path.Combine(store.Root, "dictation", "recordings", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            microphone = new Microphone(path, store.Settings.MicrophoneId); microphoneTimer.Start();
            DictationNote("Recording · " + targetMessage); UpdateDictationState();
        }
        catch (Exception error) { DictationNote(error.Message); }
    }
    private async void StopDictation(object sender, RoutedEventArgs e)
    {
        if (microphone == null) return;
        var recording = microphone; microphone = null; microphoneTimer.Stop();
        try
        {
            recording.Dispose(); WorkbenchCircle.Width = WorkbenchCircle.Height = 22; widget?.SetLevel(0, recording.Seconds);
            if (recording.Seconds < .3) throw new IOException("Record a little longer before transcribing.");
            await RunDictation(recording.Path, dictationTarget);
        }
        catch (Exception error) { DictationNote(error.Message); }
        finally { UpdateDictationState(); }
    }
    private async Task RunDictation(string path, DictationTarget? target)
    {
        var token = BeginOperation(); dictationProcessing = true; UpdateDictationState();
        try
        {
            var engine = new Dictation(store, models, processes, pipeline);
            dictationResult = await engine.Transcribe(path, recordingModel!, recordingLanguage, recordingRewrite, recordingLlm, recordingVocabulary, DictationNote, token, recordingPrompt);
            JsonFiles.Write(Path.Combine(store.Root, "dictation", "latest.json"), dictationResult);
            ShowDictationResult();
            if (SpeechPage.IsSelected && target == null) FinalDictation.BringIntoView();
            RefreshWorkspaces(workspace?.Id);
            var message = dictationResult.Warning ?? (dictationResult.Rewritten ? "Cleanup complete · review the result below" : "Transcription complete");
            if (target != null && dictationResult.Warning == null && !string.IsNullOrWhiteSpace(dictationResult.Text)) { DictationNote("Inserting in the selected text box…"); await target.Insert(dictationResult.Text); message = "Inserted in the selected text box · result saved in Speech-to-text"; }
            DictationNote(message);
        }
        catch (OperationCanceledException) { DictationNote("Canceled. The recording and any completed raw transcript are preserved."); }
        catch (Exception error) { DictationNote(error.Message); }
        finally { dictationTarget = null; dictationProcessing = false; EndOperation(); UpdateDictationState(); }
    }
    private void CancelDictation(object sender, RoutedEventArgs e)
    {
        microphoneTimer.Stop(); if (microphone != null) { try { microphone.Dispose(); } catch (Exception error) { DictationNote(error.Message); } microphone = null; }
        if (dictationProcessing) operation?.Cancel(); dictationTarget = null; WorkbenchCircle.Width = WorkbenchCircle.Height = 22; widget?.SetLevel(0, 0);
        DictationNote("Canceled · recording kept locally"); UpdateDictationState();
    }
    private async void TestDictationFile(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireIdle(); SaveDictationPreferences(); var picker = new Microsoft.Win32.OpenFileDialog { Title = "Test a recording with the selected speech model", Filter = "Audio/video|*.wav;*.mp3;*.mp4;*.m4a;*.flac;*.ogg;*.mkv|All files|*.*" };
            if (picker.ShowDialog(this) != true) return;
            recordingModel = DictationModelBox.SelectedItem as Model ?? throw new IOException("Choose an installed speech model."); recordingLlm = DictationLlmBox.SelectedItem as Model;
            recordingRewrite = store.Settings.RewriteDictation; recordingLanguage = store.Settings.DictationLanguage; recordingVocabulary = store.Settings.VocabularyEnabled ? VocabularyMap.Validate(store.Settings.Vocabulary) : [];
            recordingPrompt = SelectedSpeechPrompt() with { };
            await RunDictation(picker.FileName, null);
        }
        catch (Exception error) { DictationNote(error.Message); }
    }
    private void CopyDictation(object sender, RoutedEventArgs e) { try { if (!string.IsNullOrWhiteSpace(FinalDictation.Text)) { Clipboard.SetText(FinalDictation.Text); DictationNote("Result copied"); } } catch (Exception error) { DictationNote(error.Message); } }
    private void RestoreDictationClipboard(object sender, RoutedEventArgs e) { try { if (DictationTarget.ClipboardWasChanged && MessageBox.Show(this, "Restore the previous clipboard and replace its newer contents?", "Restore clipboard", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; DictationTarget.RestoreClipboard(true); UpdateDictationState(); DictationNote("Previous clipboard restored"); } catch (Exception error) { DictationNote(error.Message); } }
    private void OpenDictationFolder(object sender, RoutedEventArgs e) { var path = dictationResult?.Directory ?? Path.Combine(store.Root, "dictation"); Directory.CreateDirectory(path); Open(path); }
    private void ShowTutorial(object sender, RoutedEventArgs e) { HelpPage.IsSelected = true; TutorialPanel.Visibility = Visibility.Visible; TutorialPanel.BringIntoView(); }
    private void FinishTutorial(object sender, RoutedEventArgs e) { store.Settings.TutorialVersion = 1; store.SaveSettings(); TutorialPanel.Visibility = Visibility.Collapsed; SpeechPage.IsSelected = true; }
    private void OpenSpeechPage(object sender, RoutedEventArgs e) => SpeechPage.IsSelected = true;
    private void OpenModelsPage(object sender, RoutedEventArgs e) => ModelsPage.IsSelected = true;

    private SpeechPrompt SelectedSpeechPrompt() => SpeechPromptBox.SelectedItem as SpeechPrompt ?? throw new IOException("Choose a speech processing prompt.");
    private void SavePromptEditor(SpeechPrompt prompt)
    {
        var edited = prompt with { Name = SpeechPromptName.Text, SystemInstructions = SpeechPromptInstructions.Text, AllowMarkdown = SpeechPromptMarkdown.IsChecked == true };
        edited = SpeechPrompts.Validate(speechPrompts.Select(p => p.Id == prompt.Id ? edited : p)).Single(p => p.Id == prompt.Id);
        prompt.Name = edited.Name; prompt.SystemInstructions = edited.SystemInstructions; prompt.AllowMarkdown = edited.AllowMarkdown;
    }
    private void LoadSpeechPrompt()
    {
        var prompt = SelectedSpeechPrompt(); SpeechPromptName.Text = prompt.Name; SpeechPromptInstructions.Text = prompt.SystemInstructions; SpeechPromptMarkdown.IsChecked = prompt.AllowMarkdown;
        DeleteSpeechPromptButton.IsEnabled = prompt.Id is not ("light-cleanup" or "document"); RefreshWidgetLabel();
    }
    private void SpeechPromptChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || changingSpeechPrompt) return;
        var previous = e.RemovedItems.OfType<SpeechPrompt>().FirstOrDefault();
        try { if (previous != null) SavePromptEditor(previous); LoadSpeechPrompt(); }
        catch (Exception error) { changingSpeechPrompt = true; SpeechPromptBox.SelectedItem = previous; changingSpeechPrompt = false; DictationNote(error.Message); }
    }
    private void CommitSpeechPrompts()
    {
        SavePromptEditor(SelectedSpeechPrompt()); store.Settings.SpeechPrompts = SpeechPrompts.Validate(speechPrompts); store.Settings.SpeechPromptId = SelectedSpeechPrompt().Id;
        changingSpeechPrompt = true; SpeechPromptBox.Items.Refresh(); changingSpeechPrompt = false; RefreshWidgetLabel();
    }
    private void SaveSpeechPrompt(object sender, RoutedEventArgs e) { try { CommitSpeechPrompts(); store.SaveSettings(); DictationNote("Prompt saved. The widget uses this choice when language-model processing is on."); } catch (Exception error) { DictationNote(error.Message); } }
    private void AddSpeechPrompt(object sender, RoutedEventArgs e)
    {
        try { CommitSpeechPrompts(); var number = 1; while (speechPrompts.Any(p => p.Name.Equals("Custom prompt " + number, StringComparison.OrdinalIgnoreCase))) number++; var prompt = SelectedSpeechPrompt() with { Id = Guid.NewGuid().ToString("N"), Name = "Custom prompt " + number }; speechPrompts.Add(prompt); SpeechPromptBox.SelectedItem = prompt; CommitSpeechPrompts(); store.SaveSettings(); SpeechPromptEditor.IsExpanded = true; }
        catch (Exception error) { DictationNote(error.Message); }
    }
    private void DeleteSpeechPrompt(object sender, RoutedEventArgs e)
    {
        try { var prompt = SelectedSpeechPrompt(); if (prompt.Id is "light-cleanup" or "document") return; changingSpeechPrompt = true; speechPrompts.Remove(prompt); SpeechPromptBox.SelectedItem = speechPrompts[0]; changingSpeechPrompt = false; LoadSpeechPrompt(); CommitSpeechPrompts(); store.SaveSettings(); }
        catch (Exception error) { changingSpeechPrompt = false; DictationNote(error.Message); }
    }
    private void ResetSpeechPrompt(object sender, RoutedEventArgs e)
    {
        try { var prompt = SelectedSpeechPrompt(); var defaults = SpeechPrompts.Default(prompt.Id); prompt.SystemInstructions = defaults.SystemInstructions; prompt.AllowMarkdown = defaults.AllowMarkdown; LoadSpeechPrompt(); CommitSpeechPrompts(); store.SaveSettings(); DictationNote("Default instructions and output format restored for this prompt."); }
        catch (Exception error) { DictationNote(error.Message); }
    }
    private void RefreshWidgetLabel() { if (widget != null) widget.ModelName.Text = ((DictationModelBox.SelectedItem as Model)?.Name ?? "Install a speech model in Models") + "\nPrompt: " + (SpeechPromptBox.SelectedItem as SpeechPrompt)?.Name; }
    private void ShowDictationResult()
    {
        if (dictationResult == null) return;
        RawDictation.Text = dictationResult.Raw; MappedDictation.Text = dictationResult.Mapped; FinalDictation.Text = dictationResult.Text;
        var speechModel = dictationResult.AsrModelName ?? "Previous speech run";
        var languageModel = models.Catalogue.FirstOrDefault(m => m.Id == dictationResult.CleanupModelId)?.Name ?? dictationResult.CleanupModelId;
        SpeechResultInfo.Text = dictationResult.CleanupRequested ? "Last run: " + speechModel + " → " + languageModel + " · " + (dictationResult.Prompt?.Name ?? "Light cleanup (1.1)") + " · " + (dictationResult.Prompt?.AllowMarkdown == true ? "Markdown allowed" : "plain text") : "Last run: " + speechModel + " · transcription only" + (dictationResult.AsrSeconds != null ? " · segment line breaks removed" : "");
        SpeechTiming.Text = DictationTiming(dictationResult); if (widget != null) widget.TimingText.Text = SpeechTiming.Text;
    }
    private async void ApplySpeechPrompt(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireIdle(); SaveDictationPreferences(); if (dictationResult == null) throw new IOException("Record or load a test first.");
            var token = BeginOperation(); dictationProcessing = true; UpdateDictationState();
            try { dictationResult = await new Dictation(store, models, processes, pipeline).Process(dictationResult, store.Settings.RewriteDictation, DictationLlmBox.SelectedItem as Model, store.Settings.VocabularyEnabled ? VocabularyMap.Validate(store.Settings.Vocabulary) : [], SelectedSpeechPrompt() with { }, DictationNote, token); JsonFiles.Write(Path.Combine(store.Root, "dictation", "latest.json"), dictationResult); ShowDictationResult(); DictationNote(dictationResult.Warning ?? "Selected prompt applied to the same raw speech · compare both columns"); }
            finally { dictationProcessing = false; EndOperation(); UpdateDictationState(); }
        }
        catch (OperationCanceledException) { DictationNote("Canceled · previous result and raw speech retained."); }
        catch (Exception error) { DictationNote(error.Message); }
    }
    private static string DictationTiming(DictationResult result) => "Speech-to-text: " + (result.AsrSeconds is double asr ? asr.ToString("N2") + " s" : "—") + " · LLM: " + (result.LlmSeconds is double llm ? llm.ToString("N2") + " s" : result.CleanupRequested ? "—" : "off");
}
