using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalTranscriber;

public sealed class Pipeline(Store store, ModelManager models, Processes processes) : IDisposable
{
    // ponytail: one heavy model at a time; per-device scheduling only if measured demand requires it.
    public SemaphoreSlim InferenceGate { get; } = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, CancellationTokenSource> running = [];
    private readonly object sync = new();
    public event Action<Job>? Changed;
    public bool Busy => InferenceGate.CurrentCount == 0;
    public async Task<Media> Probe(string source, string workspaceId, CancellationToken token)
    {
        var file = new FileInfo(Path.GetFullPath(source));
        if (!file.Exists) throw new IOException("The recording is unavailable. Locate the source file.");
        var (output, _) = await processes.Run(processes.Tool("ffmpeg", "ffprobe.exe"), ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_format", "-show_streams", "-of", "json", file.FullName], token);
        using var json = JsonDocument.Parse(output); var root = json.RootElement;
        var tracks = new List<AudioTrack>(); bool video = false;
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = stream.GetProperty("codec_type").GetString(); if (type == "video") video = true;
            if (type == "audio") tracks.Add(new(stream.GetProperty("index").GetInt32(), stream.TryGetProperty("codec_name", out var codec) ? codec.GetString() ?? "unknown" : "unknown", stream.TryGetProperty("channels", out var channels) ? channels.GetInt32() : 0, stream.TryGetProperty("sample_rate", out var rate) ? int.Parse(rate.GetString()!, CultureInfo.InvariantCulture) : 0, stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var lang) ? lang.GetString() ?? "" : ""));
        }
        if (tracks.Count == 0) throw new IOException("This file has no decodable audio track.");
        if (!root.GetProperty("format").TryGetProperty("duration", out var length) || !double.TryParse(length.GetString(), CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0) throw new IOException("The recording has no valid duration. Convert it to a seekable audio file first.");
        return new(Guid.NewGuid().ToString("N"), workspaceId, file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, duration, tracks, video);
    }
    public Job Enqueue(Workspace workspace, Media source, Model model, int track, string language, string cleanup, string hints, double vad = .5)
    {
        if (!workspace.Available) throw new IOException("Folder unavailable. Locate the workspace before starting a job.");
        if (!models.Installed(model)) throw new IOException("Choose or download a speech model.");
        if (!source.Tracks.Any(t => t.Index == track)) throw new IOException("Select a valid audio track.");
        if (language != "auto" && (!model.ForcedLanguage || !model.Languages.Contains(language))) throw new IOException("This model cannot force the selected language. Use Auto detect or choose Whisper.");
        if (!Hardware.Compatible) throw new IOException("This portable build requires Windows x64 and AVX2 CPU instructions.");
        var output = store.OutputRoot(workspace, out var fallback);
        var id = Guid.NewGuid().ToString("N");
        var job = new Job { Id = id, Directory = Path.Combine(output, "jobs", id), Source = source, WorkspaceId = workspace.Id, ModelId = model.Id, ModelRevision = model.Revision, TrackIndex = track, Language = language, Cleanup = cleanup, Hints = model.ForcedLanguage ? hints : "", Threads = store.Settings.Threads, VadThreshold = vad, Stage = fallback ? "Queued — output uses app-data fallback" : "Queued" };
        job = job with { Vocabulary = store.Settings.VocabularyEnabled ? VocabularyMap.Validate(store.Settings.Vocabulary) : [] };
        store.SaveJob(job); Queue(job); return job;
    }
    public void Queue(Job job)
    {
        lock (sync) { if (running.ContainsKey(job.Id)) return; var token = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); running[job.Id] = token; _ = Task.Run(() => Run(job, token)); }
    }
    public void Cancel(string id)
    {
        lock (sync) { if (running.TryGetValue(id, out var token)) token.Cancel(); }
    }
    public void Resume(Job job) { job.State = JobState.Queued; job.Error = null; SetStage(job, JobState.Queued, "Queued for resume"); Queue(job); }
    private void SetStage(Job job, JobState state, string stage) { job.State = state; job.Stage = stage; store.SaveJob(job); Changed?.Invoke(job); }
    public async Task Execute(Job job, CancellationToken token)
    {
        if (!Hardware.Compatible) throw new IOException("This portable build requires Windows x64 and AVX2 CPU instructions.");
        var file = new FileInfo(job.Source.Path);
        if (!file.Exists || file.Length != job.Source.Size || file.LastWriteTimeUtc.Ticks != job.Source.ModifiedTicks) throw new IOException("The source file changed. Import it again and create a new job.");
        var model = models.Catalogue.Single(m => m.Id == job.ModelId);
        if (model.Revision != job.ModelRevision) throw new IOException("The model revision changed. Create a new job.");
        if (Hardware.Memory().Free < (model.EstimatedMemoryMb + 1500) * 1048576L) throw new IOException("Not enough available RAM. Close other apps or choose a smaller model.");
        SetStage(job, JobState.Preparing, "Verifying model and source"); await models.Verify(model, token);
        var startedAt = DateTime.UtcNow;
        JsonFiles.Write(Path.Combine(job.Directory, "model.json"), model);
        var temp = Path.Combine(job.Directory, "temporary"); Directory.CreateDirectory(temp);
        var audio = Path.Combine(temp, "decoded.wav");
        var warnings = new List<string>();
        if (Path.GetFullPath(job.Directory).StartsWith(Path.Combine(store.Root, "workspaces") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) warnings.Add("The workspace is not writable; results use the app-data fallback. Open output folder to find them.");
        if (Path.GetPathRoot(job.Directory) != Path.GetPathRoot(job.Source.Path)) warnings.Add("Output is on a different drive from the source.");
        var filter = "";
        if (job.Cleanup == "Enhanced") { filter = "afftdn=nf=-25,alimiter=limit=0.89:level=0"; warnings.Add("Enhanced audio uses FFmpeg FFT noise reduction; it may affect speech. Compare with Off when accuracy is uncertain."); }
        else if (job.Cleanup == "Automatic")
        {
            SetStage(job, JobState.Enhancing, "Assessing a 20-second audio sample");
            var (_, measured) = await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), ["-nostdin", "-v", "info", "-protocol_whitelist", "file,pipe", "-i", job.Source.Path, "-map", $"0:{job.TrackIndex}", "-t", "20", "-af", "volumedetect", "-f", "null", "-"], token);
            var peak = Regex.Match(measured, @"max_volume:\s*(-?[\d.]+) dB"); var mean = Regex.Match(measured, @"mean_volume:\s*(-?[\d.]+) dB");
            if (peak.Success && mean.Success && double.Parse(peak.Groups[1].Value, CultureInfo.InvariantCulture) < -12 && double.Parse(mean.Groups[1].Value, CultureInfo.InvariantCulture) > -50) { filter = "volume=3dB,alimiter=limit=0.89:level=0"; warnings.Add("Automatic cleanup applied a measured +3 dB gain with a limiter."); }
            else warnings.Add("Automatic cleanup left decoded audio unchanged; no measured need for gain or heavy enhancement.");
        }
        if (!File.Exists(audio))
        {
            if (new DriveInfo(Path.GetPathRoot(audio)!).AvailableFreeSpace < job.Source.Duration * model.SampleRate * 2 + 256 * 1048576) throw new IOException("Not enough disk space to decode this recording. Change workspace or free space.");
            SetStage(job, JobState.Extracting, "Decoding selected audio track");
            var args = new List<string> { "-nostdin", "-v", "error", "-protocol_whitelist", "file,pipe", "-i", job.Source.Path, "-map", $"0:{job.TrackIndex}", "-vn", "-ac", "1", "-ar", model.SampleRate.ToString(CultureInfo.InvariantCulture), "-c:a", "pcm_s16le" };
            if (filter != "") args.AddRange(["-af", filter]);
            var part = audio + ".part.wav"; args.AddRange(["-progress", "pipe:1", "-y", part]);
            try { await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), args, token, line => { if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var microseconds)) { job.CompletedSeconds = microseconds / 1_000_000d; Changed?.Invoke(job); } }); }
            catch (IOException) when (filter != "") { warnings.Add("Audio enhancement failed; decoding safely retried without enhancement."); var at = args.IndexOf("-af"); args.RemoveRange(at, 2); filter = ""; await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), args, token); }
            File.Move(part, audio, true);
        }
        JsonFiles.Write(Path.Combine(job.Directory, "preprocessing.json"), new { job.Cleanup, Filter = filter, SampleRate = model.SampleRate, ChannelLayout = "mono", OriginalTracks = job.Source.Tracks, Warnings = warnings });
        var workerResult = Path.Combine(temp, "result.json");
        var request = new { runtime = model.Runtime, modelPath = model.Runtime == "onnx-asr" ? models.Location(model) : models.Weights(model), vadPath = Path.Combine(processes.RuntimeRoot, "vad"), audioPath = audio, outputPath = workerResult, checkpointDirectory = Path.Combine(job.Directory, "checkpoints"), sampleRate = model.SampleRate, language = job.Language, hints = job.Hints, threads = job.Threads, vadThreshold = job.VadThreshold, whisperExecutable = processes.Tool("whisper", "whisper-cli.exe"), signature = JsonFiles.Hash(new { job.Source, job.TrackIndex, job.ModelId, job.ModelRevision, job.Cleanup, job.Language, job.VadThreshold }) };
        var requestPath = Path.Combine(temp, "request.json"); JsonFiles.Write(requestPath, request);
        SetStage(job, JobState.Segmenting, "Silero VAD — bounded audio windows"); job.CompletedSeconds = 0;
        await processes.Run(processes.Tool("python", "python.exe"), [Path.Combine(AppContext.BaseDirectory, "workers", "asr_worker.py"), requestPath], token, line =>
        {
            try { using var evt = JsonDocument.Parse(line); if (evt.RootElement.TryGetProperty("stage", out var stage)) { job.Stage = stage.GetString() ?? "Transcribing"; job.State = job.Stage.Contains("Segmenting") ? JobState.Segmenting : JobState.Transcribing; if (evt.RootElement.TryGetProperty("completedSeconds", out var completed)) job.CompletedSeconds = completed.GetDouble(); store.SaveJob(job); Changed?.Invoke(job); } }
            catch (JsonException) { }
        });
        SetStage(job, JobState.Aligning, "Merging timed segments");
        var result = JsonFiles.Read<Transcript>(workerResult);
        var transcript = new Transcript { Id = job.Id, SourceFile = job.Source.Path, SourceTrack = job.TrackIndex, DurationMs = (long)(job.Source.Duration * 1000), Language = job.Language, ModelId = model.Id, ModelRevision = model.Revision, Segments = result.Segments, ProcessingWarnings = warnings };
        SetStage(job, JobState.Saving, "Saving immutable raw transcript"); token.ThrowIfCancellationRequested();
        var raw = Path.Combine(job.Directory, "transcript.raw.json");
        if (!File.Exists(raw)) JsonFiles.Write(raw, transcript);
        var mapped = transcript with { Version = "edited", Segments = transcript.Segments.Select(s => s with { Text = VocabularyMap.Apply(s.Text, job.Vocabulary) }).ToList() };
        JsonFiles.Write(Path.Combine(job.Directory, "vocabulary.json"), new { Rules = job.Vocabulary, Changed = mapped.FullText != transcript.FullText, Order = "ASR → vocabulary → optional dictation cleanup" });
        if (mapped.FullText != transcript.FullText && !File.Exists(Path.Combine(job.Directory, "transcript.edited.json"))) Store.SaveEdited(job, mapped);
        JsonFiles.Write(Path.Combine(job.Directory, "execution.json"), new { StartedAt = startedAt, CompletedAt = DateTime.UtcNow, WallSeconds = (DateTime.UtcNow - startedAt).TotalSeconds, RuntimeLock = JsonFiles.Read<object>(Path.Combine(AppContext.BaseDirectory, "runtime-lock.json")) });
        job.CompletedSeconds = job.Source.Duration; SetStage(job, JobState.Completed, transcript.Segments.Count == 0 ? "Completed — no speech detected" : "Completed");
        File.Delete(audio); File.Delete(workerResult); File.Delete(requestPath);
    }
    private async Task Run(Job job, CancellationTokenSource cancellation)
    {
        var held = false;
        try { await InferenceGate.WaitAsync(cancellation.Token); held = true; await Execute(job, cancellation.Token); }
        catch (OperationCanceledException) { SetStage(job, JobState.Canceled, "Canceled — resume from checkpoints"); }
        catch (Exception error) { job.Error = error.Message; SetStage(job, JobState.Failed, "Failed — " + error.Message); }
        finally { if (held) InferenceGate.Release(); lock (sync) { running.Remove(job.Id); cancellation.Dispose(); } }
    }
    public async Task<string> Preview(Media media, int track, bool enhanced, CancellationToken token)
    {
        var file = Path.Combine(store.Settings.CacheRoot, $"preview-{media.Id}-{(enhanced ? "enhanced" : "original")}.wav");
        var args = new List<string> { "-nostdin", "-v", "error", "-protocol_whitelist", "file,pipe", "-i", media.Path, "-map", $"0:{track}", "-t", "15", "-ac", "1", "-ar", "16000" };
        if (enhanced) args.AddRange(["-af", "afftdn=nf=-25,alimiter=limit=0.89:level=0"]);
        args.AddRange(["-y", file]); await processes.Run(processes.Tool("ffmpeg", "ffmpeg.exe"), args, token); return file;
    }
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
