using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LocalTranscriber;

public sealed record VocabularyRule
{
    public string Heard { get; set; } = "";
    public string Replacement { get; set; } = "";
}

public static class VocabularyMap
{
    public static List<VocabularyRule> Validate(IEnumerable<VocabularyRule> source)
    {
        var rules = source.Select(r => new VocabularyRule { Heard = r.Heard.Trim(), Replacement = r.Replacement.Trim() }).ToList();
        if (rules.Count > 200 || rules.Any(r => r.Heard.Length is < 1 or > 200 || r.Replacement.Length is < 1 or > 500 || r.Heard.Contains('\n') || r.Replacement.Contains('\n')))
            throw new IOException("Use up to 200 vocabulary entries, each with a heard phrase (1–200 characters) and replacement (1–500 characters), on one line.");
        if (rules.Select(r => r.Heard).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count) throw new IOException("Each heard phrase must be unique (ignoring letter case).");
        return rules;
    }
    public static string Apply(string text, IEnumerable<VocabularyRule> source)
    {
        var rules = Validate(source); if (rules.Count == 0) return text;
        var lookup = rules.ToDictionary(r => r.Heard, r => r.Replacement, StringComparer.OrdinalIgnoreCase);
        var alternatives = string.Join("|", rules.OrderByDescending(r => r.Heard.Length).Select(r => Regex.Escape(r.Heard)));
        // One pass prevents replacement text from being replaced again by another rule.
        return Regex.Replace(text, @"(?<![\p{L}\p{M}\p{N}_])(?:" + alternatives + @")(?![\p{L}\p{M}\p{N}_])", m => lookup[m.Value], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}

public sealed record DictationResult(string JobId, string Directory, string Raw, string Mapped, string Text, bool Rewritten, string? Warning)
{
    public bool CleanupRequested { get; init; }
    public string? CleanupModelId { get; init; }
    public string? CleanupModelRevision { get; init; }
    public List<Artifact> CleanupArtifacts { get; init; } = [];
    public List<VocabularyRule> Vocabulary { get; init; } = [];
    public string Normalized { get; init; } = "";
    public SpeechPrompt? Prompt { get; init; }
    public double? AsrSeconds { get; init; }
    public double? LlmSeconds { get; init; }
    public string? AsrModelId { get; init; }
    public string? AsrModelName { get; init; }
}

public sealed class Dictation(Store store, ModelManager models, Processes processes, Pipeline pipeline)
{
    public async Task<DictationResult> Transcribe(string recording, Model model, string language, bool rewrite, Model? llm, List<VocabularyRule> vocabulary, Action<string> progress, CancellationToken token, SpeechPrompt? prompt = null)
    {
        var asrWatch = Stopwatch.StartNew();
        vocabulary = VocabularyMap.Validate(vocabulary);
        if (!models.Installed(model) || model.Role != "asr" || !model.Available) throw new IOException("Choose an installed speech model in Speech-to-text.");
        if (language != "auto" && (!model.ForcedLanguage || !model.Languages.Contains(language))) throw new IOException("This speech model cannot force that language. Select Auto detect.");
        if (rewrite && (llm == null || !models.Installed(llm) || llm.Role != "llm")) throw new IOException("Choose an installed language model for optional cleanup, or turn cleanup off.");
        var folder = Path.Combine(store.Root, "dictation"); Directory.CreateDirectory(folder);
        var workspace = store.AddWorkspace(folder);
        var media = await pipeline.Probe(recording, workspace.Id, token); store.SaveMedia(media);
        var id = Guid.NewGuid().ToString("N");
        var job = new Job { Id = id, Source = media, WorkspaceId = workspace.Id, Directory = Path.Combine(store.OutputRoot(workspace, out _), "jobs", id), ModelId = model.Id, ModelRevision = model.Revision, TrackIndex = media.Tracks[0].Index, Language = language, Cleanup = "Off", Threads = store.Settings.Threads, Vocabulary = vocabulary };
        store.SaveJob(job);
        var held = false;
        try
        {
            await pipeline.InferenceGate.WaitAsync(token); held = true;
            progress("Transcribing locally…");
            await pipeline.Execute(job, token);
        }
        catch (Exception error) { job.State = error is OperationCanceledException ? JobState.Canceled : JobState.Failed; job.Error = error.Message; job.Stage = job.State.ToString(); store.SaveJob(job); throw; }
        finally { if (held) pipeline.InferenceGate.Release(); }
        var raw = Store.LoadTranscript(job, true).FullText;
        asrWatch.Stop();
        return await Process(new(job.Id, job.Directory, raw, "", "", false, null) { AsrSeconds = asrWatch.Elapsed.TotalSeconds, AsrModelId = model.Id, AsrModelName = model.Name }, rewrite, llm, vocabulary, prompt ?? SpeechPrompts.Default(), progress, token);
    }

    public async Task<DictationResult> Process(DictationResult source, bool rewrite, Model? llm, List<VocabularyRule> vocabulary, SpeechPrompt prompt, Action<string> progress, CancellationToken token)
    {
        vocabulary = VocabularyMap.Validate(vocabulary); prompt = SpeechPrompts.Validate([prompt]).Single();
        if (rewrite && (llm == null || llm.Role != "llm" || !llm.Available || !models.Installed(llm))) throw new IOException("Choose an installed language model or switch cleanup off.");
        var normalized = DictationText.Normalize(source.Raw);
        var mapped = VocabularyMap.Apply(normalized, vocabulary);
        var result = source with { Normalized = normalized, Mapped = mapped, Text = mapped, Rewritten = false, Warning = null, CleanupRequested = rewrite, CleanupModelId = rewrite ? llm!.Id : null, CleanupModelRevision = rewrite ? llm!.Revision : null, CleanupArtifacts = rewrite ? llm!.Artifacts : [], Vocabulary = vocabulary, Prompt = rewrite ? prompt with { } : null, LlmSeconds = null };
        var resultPath = Path.Combine(result.Directory, "dictation.json");
        if (File.Exists(resultPath)) JsonFiles.Write(Path.Combine(result.Directory, "dictation-versions", Guid.NewGuid().ToString("N") + ".json"), JsonFiles.Read<DictationResult>(resultPath));
        JsonFiles.Write(resultPath, result);
        if (string.IsNullOrWhiteSpace(mapped)) { result = result with { Warning = "No speech detected. Check the microphone and speech model." }; JsonFiles.Write(resultPath, result); return result; }
        if (rewrite)
        {
            var llmWatch = Stopwatch.StartNew();
            try
            {
                var cleaned = await Rewrite(mapped, llm!, progress, token, vocabulary, prompt);
                result = result with { Text = cleaned, Rewritten = true };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { result = result with { Warning = "Cleanup failed; vocabulary-corrected transcription is available for review. " + error.Message }; }
            finally { result = result with { LlmSeconds = llmWatch.Elapsed.TotalSeconds }; }
        }
        JsonFiles.Write(resultPath, result);
        return result;
    }

    public async Task<string> Rewrite(string text, Model model, Action<string> progress, CancellationToken token, List<VocabularyRule>? vocabulary = null, SpeechPrompt? prompt = null)
    {
        prompt = SpeechPrompts.Validate([prompt ?? SpeechPrompts.Default()]).Single();
        text = DictationText.Normalize(text);
        await pipeline.InferenceGate.WaitAsync(token);
        try
        {
            if (Hardware.Memory().Free < (model.EstimatedMemoryMb + 1000) * 1048576L) throw new IOException("Not enough free RAM for cleanup. Close other apps, choose a smaller model, or turn cleanup off.");
            progress("Verifying cleanup model…"); await models.Verify(model, token);
            var settings = new SummarySettings { ContextTokens = 8192, OutputTokens = 2048, Threads = store.Settings.Threads, Temperature = 0, RepeatPenalty = 1 };
            progress("Cleaning up locally…"); await using var server = await LocalLlm.Start(processes, models.Weights(model), settings, token);
            var messages = new[] {
                new { role = "system", content = SpeechPrompts.System(prompt) },
                new { role = "user", content = "Process the following transcribed speech using your system instructions. Vocabulary replacements already applied: " + string.Join("; ", (vocabulary ?? []).Select(r => r.Replacement).Distinct()) + "\n<dictation>\n" + text + "\n</dictation>" }
            };
            if (await server.Count(messages, token) + settings.OutputTokens + 64 >= server.ContextTokens) throw new IOException("This dictation is too long for faithful cleanup. Use transcription only or record a shorter passage.");
            var warnings = new List<string>(); var output = await server.Generate(messages, settings, warnings, token);
            if (warnings.Count > 0) throw new IOException("Cleanup reached its output limit; the original text was retained.");
            output = DictationText.Format(output, prompt.AllowMarkdown);
            if (string.IsNullOrWhiteSpace(output)) throw new IOException("Processing returned no usable text; the original text was retained.");
            if (prompt.SystemInstructions == SpeechPrompts.LightInstructions && (vocabulary ?? []).Any(rule => text.Contains(rule.Replacement, StringComparison.OrdinalIgnoreCase) && !output.Contains(rule.Replacement, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cleanup changed a vocabulary term; the original text was retained.");
            return output;
        }
        finally { pipeline.InferenceGate.Release(); }
    }
}
