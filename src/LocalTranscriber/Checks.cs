using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LocalTranscriber;
public static class Checks
{
    public static async Task<string> Run(string[] args)
    {
        var assertions = 0;
        void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception("Check failed: " + message); }
        var root = Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_DATA") ?? Path.Combine(Path.GetTempPath(), "LocalTranscriber-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var evidence = new List<object>();
        var rules = new List<VocabularyRule> { new() { Heard = "MA", Replacement = "MAA" }, new() { Heard = "MA request tracker", Replacement = "MAA request tracker" }, new() { Heard = "MAA", Replacement = "another" } };
        Assert(VocabularyMap.Apply("ma request tracker, MA. SMALL MAA", rules) == "MAA request tracker, MAA. SMALL another", "vocabulary longest match, case, boundaries and no cascade");
        Assert(VocabularyMap.Apply("Québec and a+b cost $5", [new() { Heard = "Québec", Replacement = "Montréal" }, new() { Heard = "a+b", Replacement = "$1" }]) == "Montréal and $1 cost $5", "Unicode vocabulary and literal regex/replacement characters");
        var invalidVocabulary = false; try { VocabularyMap.Validate([new() { Heard = "MA", Replacement = "x" }, new() { Heard = "ma", Replacement = "y" }]); } catch (IOException) { invalidVocabulary = true; } Assert(invalidVocabulary, "ambiguous vocabulary rejected");
        Assert(Microphone.Rms([0, 0, 0, 0]) == 0 && Math.Abs(Microphone.Rms([0, 64, 0, 192]) - .5) < .001, "PCM16 level calibration and silence");
        var migrated = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"threads\":4}", JsonFiles.Options)!;
        Assert(!migrated.RewriteDictation && migrated.Vocabulary.Count == 2 && migrated.TutorialVersion == 0, "old settings migration and optional cleanup default");
        Assert(migrated.SpeechPrompts.Count == 2 && migrated.SpeechPromptId == "light-cleanup", "1.1 settings migrate to saved speech prompts");
        Assert(DictationText.Normalize(" First.\r\n Second.\n\nThird.\t ") == "First. Second. Third.", "ASR segment breaks flatten into one paragraph");
        Assert(VocabularyMap.Apply(DictationText.Normalize("MA\nrequest tracker"), [new() { Heard = "MA request tracker", Replacement = "MAA request tracker" }]) == "MAA request tracker", "vocabulary spans former segment boundaries");
        var markdown = "# Réunion\n\n**Marie** attend _Friday_.\n\n- Keep `MAA` unchanged.\n- ~~Old~~ Nouveau.\n\n```text\nCode stays.\n```";
        var plain = DictationText.Format(markdown, false);
        Assert(plain.Contains("Réunion") && plain.Contains("Marie") && plain.Contains("Friday") && plain.Contains("MAA") && plain.Contains("Code stays.") && !plain.Contains("# ") && !plain.Contains("**") && !plain.Contains("~~") && !plain.Contains('`'), "Markdown strips formatting while retaining content and Unicode");
        Assert(plain.Replace("\r\n", "\n").Contains("\n\n"), "plain-text formatting preserves paragraphs");
        Assert(DictationText.Format(markdown, true) == markdown, "Markdown-enabled output stays unchanged");
        Assert(DictationText.Format("file_name costs $2,000 and 2 * 3 = 6.", false).Contains("file_name costs $2,000 and 2 * 3 = 6."), "plain-text names, arithmetic and amounts preserved");
        Assert(DictationText.Format("See [MAA](https://example.com/tracker).", false).Contains("MAA (https://example.com/tracker)"), "plain-text link destination preserved");
        Assert(DictationText.Format("| Owner | Date |\n| --- | --- |\n| Marie | Friday |", false).Contains("Marie\tFriday"), "plain-text table retains separated cells");
        var overridePrompt = new SpeechPrompt { Name = "My override", SystemInstructions = "Output exactly my chosen text.", AllowMarkdown = false };
        Assert(SpeechPrompts.System(overridePrompt).StartsWith(overridePrompt.SystemInstructions) && !SpeechPrompts.System(overridePrompt).Contains(SpeechPrompts.LightInstructions) && SpeechPrompts.System(overridePrompt).Contains("Plain text only"), "system prompt is fully overridden with separate output policy");
        Assert(SpeechPrompts.System(overridePrompt with { AllowMarkdown = true }).Contains("Markdown is allowed"), "Markdown permission reaches the model");
        var invalidPrompt = false; try { SpeechPrompts.Validate([overridePrompt, overridePrompt with { Id = "other" }]); } catch (IOException) { invalidPrompt = true; } Assert(invalidPrompt, "duplicate prompt names rejected");
        invalidPrompt = false; try { SpeechPrompts.Validate([overridePrompt with { SystemInstructions = " " }]); } catch (IOException) { invalidPrompt = true; } Assert(invalidPrompt, "empty system prompt rejected");
        var catalogue = JsonFiles.Read<List<Model>>(Path.Combine(AppContext.BaseDirectory, "catalogue.json"));
        Assert(catalogue.Where(m => m.Available).SelectMany(m => m.Artifacts).All(a => ModelManager.AllowedDownloadUri(new Uri(a.Url))), "all pinned catalogue model and licence URLs are permitted");
        Assert(new[] { "http://www.apache.org/licenses/LICENSE-2.0.txt", "https://www.apache.org/other.txt", "https://www.apache.org.evil.example/licenses/LICENSE-2.0.txt", "https://huggingface.co.evil.example/model", "https://example.com/model" }.All(url => !ModelManager.AllowedDownloadUri(new Uri(url))) && ModelManager.AllowedDownloadUri(new Uri("https://cas-bridge.xethub.hf.co/model")), "download source guard rejects insecure/unrelated/lookalike hosts and paths while retaining Hugging Face CDN redirects");
        Assert(new[] { "qwen35-0.8b", "qwen35-2b", "qwen35-4b", "gemma4-e2b", "gemma4-e4b" }.All(id => catalogue.Any(m => m.Id == id && m.Available && m.Artifacts.Any(a => a.Name.EndsWith(".gguf") && a.Sha256.Length == 64 && a.Size > 0 && a.Url.Contains(m.Revision)))), "requested compact models are downloadable with pinned hashes");
        var t = new Transcript { DurationMs = 10_000, Segments = [new() { SegmentId = "s000001", StartMs = 1234, EndMs = 3000, Text = "Bonjour, \"Québec\".\nÇa va?", SpeakerId = "Marie" }, new() { SegmentId = "s000002", StartMs = 2900, EndMs = 5000, Text = "Décision: vendredi." }] };
        Assert(Exporter.Time(3_600_001, ',') == "01:00:00,001", "hour timestamp");
        foreach (var format in new[] { "txt", "md", "json", "csv", "srt", "vtt" }) { var text = Exporter.Transcript(t, format); Assert(text.Contains("Québec") || format == "json" && text.Contains("\\u00E9"), format + " UTF-8 accents"); Exporter.Save(Path.Combine(root, "export." + format), text); }
        Assert(Exporter.Transcript(t, "csv").Contains("\"\"Québec\"\""), "CSV quote escaping");
        Assert(!Exporter.Transcript(t, "txt", TimestampStyle.None, false).Contains("Marie:"), "speaker option");
        Assert(Exporter.Transcript(t, "srt").Contains("00:00:03,000 --> 00:00:05,000"), "overlap normalization");
        Assert(Exporter.Transcript(t, "vtt").StartsWith("WEBVTT\n"), "WebVTT header");
        var rejected = false; try { ModelManager.OwnedPath(root, "../escape"); } catch (IOException) { rejected = true; } Assert(rejected, "path traversal rejection");
        var badGguf = Path.Combine(root, "bad.gguf"); File.WriteAllBytes(badGguf, [0, 1, 2, 3, 4, 5, 6, 7]); rejected = false; try { Gguf.Read(badGguf); } catch (IOException) { rejected = true; } Assert(rejected, "malformed GGUF rejection");
        var workspacePath = Path.Combine(root, "workspace"); Directory.CreateDirectory(workspacePath); File.WriteAllText(Path.Combine(workspacePath, "original.txt"), "preserve");
        string workspaceId;
        var secondPath = Path.Combine(root, "réunions-françaises"); Directory.CreateDirectory(secondPath);
        var recoveryDirectory = Path.Combine(root, "recovery-check");
        using (var index = new Store(Path.Combine(root, "unit-index"))) { var w = index.AddWorkspace(workspacePath); workspaceId = w.Id; index.UpdateWorkspace(w with { Label = "Québec" }); index.AddWorkspace(secondPath); index.SaveJob(new Job { Id = "recovery", Directory = recoveryDirectory, WorkspaceId = w.Id, ModelId = "test", Source = new Media("test", w.Id, Path.Combine(workspacePath, "original.txt"), 8, 0, 1, [], false), State = JobState.Transcribing }); }
        using (var index = new Store(Path.Combine(root, "unit-index"))) { Assert(index.Workspaces().Single(w => w.Id == workspaceId).Label == "Québec" && index.Workspaces().Count == 2, "multiple Unicode workspace persistence"); Assert(index.Jobs().Single(j => j.Id == "recovery").State == JobState.Interrupted, "unfinished job recovery on restart"); index.RemovePointer(workspaceId); Assert(File.Exists(Path.Combine(workspacePath, "original.txt")) && index.Workspaces().Single().Path == secondPath, "pointer deletion preserves files and other workspace"); }
        using (var owner = new Processes()) { using var child = owner.Start(owner.Tool("python", "python.exe"), ["-c", "import time; time.sleep(60)"]); owner.Dispose(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await child.WaitForExitAsync(deadline.Token); Assert(child.HasExited, "worker killed when owner closes"); }
        JsonFiles.Write(Path.Combine(root, "atomic.json"), t); Assert(JsonFiles.Read<Transcript>(Path.Combine(root, "atomic.json")).Segments.Count == 2, "atomic JSON round trip");
        Assert(!Directory.EnumerateFiles(root, "*.tmp").Any(), "no unfinished atomic writes");
        evidence.Add(new { Check = "Unit/invariant checks", Result = "Passed", Cases = assertions });
        if (args.Contains("--smoke"))
        {
            string Arg(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            var fixtures = Arg("--fixtures", Path.Combine(AppContext.BaseDirectory, "fixtures"));
            using var store = new Store(Path.Combine(root, "integration-index")); var models = new ModelManager(store); models.LoadImports(); using var processes = new Processes(); using var pipeline = new Pipeline(store, models, processes); var summarizer = new Summarizer(models, processes, pipeline);
            var w = store.AddWorkspace(workspacePath);
            foreach (var file in new[] { "english.wav", "english.mp3", "english.mp4", "english.mkv" })
            { var media = await pipeline.Probe(Path.Combine(fixtures, file), w.Id, CancellationToken.None); Assert(media.Duration > 1 && media.Tracks.Count > 0, "ffprobe " + file); evidence.Add(new { Check = "Probe " + file, Result = "Passed", media.Duration, Tracks = media.Tracks.Count }); }
            foreach (var modelId in Arg("--asr", "parakeet-v3-int8,whisper-small").Split(','))
            {
                var model = models.Catalogue.Single(m => m.Id == modelId);
                await models.Download(model, (_, _) => { }, CancellationToken.None);
                foreach (var fixture in new[] { "english.wav", "french.wav" }.Where(f => File.Exists(Path.Combine(fixtures, f))))
                {
                    var media = await pipeline.Probe(Path.Combine(fixtures, fixture), w.Id, CancellationToken.None);
                    var dir = Path.Combine(workspacePath, ".localtranscriber", "jobs", Guid.NewGuid().ToString("N"));
                    var job = new Job { Source = media, WorkspaceId = w.Id, Directory = dir, ModelId = model.Id, ModelRevision = model.Revision, TrackIndex = media.Tracks[0].Index, Threads = Math.Min(8, Environment.ProcessorCount / 2), Cleanup = "Off", Language = model.ForcedLanguage ? fixture.StartsWith("french") ? "fr" : "en" : "auto" };
                    var originalHash = await ModelManager.Hash(media.Path, CancellationToken.None);
                    var watch = Stopwatch.StartNew(); await pipeline.Execute(job, CancellationToken.None); watch.Stop();
                    var transcript = Store.LoadTranscript(job, true);
                    Assert(transcript.Segments.Count > 0 && transcript.FullText.Length > 8, "real nonempty ASR " + modelId);
                    Assert(transcript.Segments.All(s => s.StartMs >= 0 && s.EndMs > s.StartMs && s.EndMs <= transcript.DurationMs + 1000), "ASR timing");
                    Assert(await ModelManager.Hash(media.Path, CancellationToken.None) == originalHash, "source unchanged");
                    foreach (var format in new[] { "txt", "md", "json", "csv", "srt", "vtt" }) Exporter.Save(Path.Combine(dir, "transcript." + format), Exporter.Transcript(transcript, format));
                    var rawHash = await ModelManager.Hash(Path.Combine(dir, "transcript.raw.json"), CancellationToken.None); var edited = JsonFiles.Read<Transcript>(Path.Combine(dir, "transcript.raw.json")); edited.Segments[0].Text += " [human edit]"; Store.SaveEdited(job, edited); Assert(rawHash == await ModelManager.Hash(Path.Combine(dir, "transcript.raw.json"), CancellationToken.None), "raw preserved after editing");
                    evidence.Add(new { Check = "ASR " + modelId + " " + fixture, Result = "Passed", WallSeconds = watch.Elapsed.TotalSeconds, AudioSeconds = media.Duration, RealTimeFactor = watch.Elapsed.TotalSeconds / media.Duration, Segments = transcript.Segments.Count, Raw = Path.Combine(dir, "transcript.raw.json"), CpuOnly = true, WorkerMetrics = processes.Metrics.ToArray() }); processes.Metrics.Clear();
                    if (modelId == "parakeet-v3-int8" && fixture == "english.wav")
                    {
                        foreach (var llmId in Arg("--llm", "lfm25-1.2b-instruct,lfm25-2.6b").Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var llm = models.Catalogue.Single(m => m.Id == llmId); await models.Download(llm, (_, _) => { }, CancellationToken.None);
                            var result = await summarizer.Generate(job, transcript, llm, new SummarySettings { Context = "A project meeting. Local Transcriber is the project name.", Template = "Decisions and actions", OutputTokens = 256, Threads = 8 }, _ => { }, CancellationToken.None);
                            Assert(result.Text.Length > 8, "real summary " + llmId); Exporter.Save(Path.Combine(dir, llmId + "-summary.md"), result.Text);
                            evidence.Add(new { Check = "Summary " + llmId, Result = "Passed", result.Id, result.Warnings, ResultPath = Path.Combine(dir, llmId + "-summary.md") });
                        }
                    }
                }
            }
            var corrupt = Path.Combine(root, "bad.wav"); File.WriteAllText(corrupt, "not audio"); rejected = false; try { await pipeline.Probe(corrupt, w.Id, CancellationToken.None); } catch (IOException) { rejected = true; } Assert(rejected, "corrupt media fails safely");
            evidence.Add(new { Check = "Corrupt media rejection", Result = "Passed" });
            if (args.Contains("--extended"))
            {
                var parakeet = models.Catalogue.Single(m => m.Id == "parakeet-v3-int8");
                var downloadProbe = parakeet with { Id = "artifact-resume-check", Artifacts = [parakeet.Artifacts.Single(a => a.Name == "vocab.txt")] };
                using (var interruption = new CancellationTokenSource()) { var interrupted = false; try { await models.Download(downloadProbe, (bytes, _) => { if (bytes > 0) interruption.Cancel(); }, interruption.Token); } catch (OperationCanceledException) { interrupted = true; } Assert(interrupted && !models.Installed(downloadProbe), "interrupted download is not installed"); }
                await models.Download(downloadProbe, (_, _) => { }, CancellationToken.None); await models.Verify(downloadProbe, CancellationToken.None); await models.Delete(downloadProbe, CancellationToken.None);
                evidence.Add(new { Check = "Interrupted HTTPS artifact download / resume / hash verification", Result = "Passed", Note = "Pinned vocabulary asset; this tests installer integrity, not recognition capability." });
                var longAudio = Path.Combine(root, "long-60m.wav");
                await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), ["-nostdin", "-v", "error", "-i", Path.Combine(fixtures, "english.wav"), "-filter_complex", "[0:a]asplit=2[a][b];[a]apad=pad_dur=3600[c];[b]adelay=3600000:all=1[d];[c][d]amix=inputs=2:duration=longest:normalize=0", "-ac", "1", "-ar", "16000", "-y", longAudio], CancellationToken.None);
                var media = await pipeline.Probe(longAudio, w.Id, CancellationToken.None);
                var job = new Job { Source = media, WorkspaceId = w.Id, Directory = Path.Combine(workspacePath, ".localtranscriber/jobs", Guid.NewGuid().ToString("N")), ModelId = parakeet.Id, ModelRevision = parakeet.Revision, TrackIndex = 0, Threads = 8, Cleanup = "Off" };
                using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                void Interrupt(Job update) { if (update.Id == job.Id && update.State == JobState.Transcribing && update.CompletedSeconds > 0) cancel.Cancel(); }
                pipeline.Changed += Interrupt;
                var canceled = false;
                try { await pipeline.Execute(job, cancel.Token); } catch (OperationCanceledException) { canceled = true; } finally { pipeline.Changed -= Interrupt; }
                Assert(canceled && !File.Exists(Path.Combine(job.Directory, "transcript.raw.json")), "cancellation prevents incomplete final output");
                Assert(Directory.GetFiles(Path.Combine(job.Directory, "checkpoints"), "chunk-*.json").Length > 0, "completed chunk checkpoint preserved");
                var timer = Stopwatch.StartNew(); await pipeline.Execute(job, CancellationToken.None); timer.Stop();
                var result = Store.LoadTranscript(job, true); Assert(media.Duration >= 3600 && result.Segments.Any(s => s.StartMs >= 3_600_000), "long recording source offsets preserved");
                evidence.Add(new { Check = "60-minute source / cancel / checkpoint resume", Result = "Passed", AudioSeconds = media.Duration, ResumeWallSeconds = timer.Elapsed.TotalSeconds, WorkerMetrics = processes.Metrics.ToArray(), Note = "Synthetic speech at beginning and end, mostly silence; not a continuous one-hour speech throughput benchmark." }); processes.Metrics.Clear();
                var llm = models.Catalogue.Single(m => m.Id == "lfm25-1.2b-instruct");
                var longerTranscript = result with { Segments = Enumerable.Range(0, 30).SelectMany(i => result.Segments.Select((s, j) => s with { SegmentId = $"s{i * result.Segments.Count + j + 1:000000}", Text = s.Text + " This is a repeated synthetic benchmark passage, not a new commitment." })).ToList() };
                var reduced = await summarizer.Generate(job, longerTranscript, llm, new SummarySettings { ContextTokens = 1024, OutputTokens = 128, Threads = 8, Template = "Key points" }, _ => { }, CancellationToken.None);
                Assert(reduced.Warnings.Any(x => x.Contains("chunked")) && Directory.Exists(Path.Combine(job.Directory, "summary-checkpoints")), "token-budget map/reduce checkpoints");
                evidence.Add(new { Check = "Actual-token long-summary map/reduce", Result = "Passed", reduced.Warnings, reduced.Id });
                var importSource = models.Weights(llm);
                var imported = await models.Import(importSource, llm, async (_, dir, token) => { await using var local = await LocalLlm.Start(processes, Directory.GetFiles(dir, "*.gguf").Single(), new SummarySettings { Threads = 8 }, token); await local.Generate(new[] { new { role = "user", content = "Say ready." } }, new SummarySettings { OutputTokens = 64 }, [], token); }, CancellationToken.None);
                Assert(models.Installed(imported), "valid GGUF import with inference");
                var reclaimed = await models.Delete(imported, CancellationToken.None); Assert(reclaimed > 600_000_000 && !models.Installed(imported), "model deletion shrinks owned storage");
                evidence.Add(new { Check = "Isolated valid GGUF import / removal", Result = "Passed", ReclaimedBytes = reclaimed });
                foreach (var mode in new[] { "Off", "Automatic", "Enhanced" })
                {
                    var source = await pipeline.Probe(Path.Combine(fixtures, "noisy-english.wav"), w.Id, CancellationToken.None);
                    var sourceHash = await ModelManager.Hash(source.Path, CancellationToken.None);
                    var cleaned = job with { Id = Guid.NewGuid().ToString("N"), Source = source, Directory = Path.Combine(workspacePath, ".localtranscriber/jobs", Guid.NewGuid().ToString("N")), Cleanup = mode };
                    await pipeline.Execute(cleaned, CancellationToken.None); Assert(Store.LoadTranscript(cleaned, true).Segments.Count > 0 && sourceHash == await ModelManager.Hash(source.Path, CancellationToken.None), "cleanup " + mode);
                    evidence.Add(new { Check = "Noisy audio cleanup " + mode, Result = "Passed", Provenance = Path.Combine(cleaned.Directory, "preprocessing.json"), Note = "Deterministic pink-noise mixture; successful operation does not prove better recognition quality." });
                }
            }
        }
        return JsonSerializer.Serialize(new { Date = DateTime.UtcNow, Hardware = Hardware.Cpu, Ram = Hardware.Memory().Total, Assertions = assertions, Evidence = evidence }, JsonFiles.Options);
    }
}
