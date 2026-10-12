using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalTranscriber;

public sealed class LocalLlm : IAsyncDisposable
{
    private readonly Process process;
    private readonly HttpClient client;
    private readonly Task<string> stdout, stderr;
    public int ContextTokens { get; }
    private LocalLlm(Processes processes, string modelPath, SummarySettings settings)
    {
        var metadata = Gguf.Read(modelPath);
        if (!metadata.TryGetValue("general.architecture", out var architecture) || architecture is not string name) throw new IOException("The GGUF has no recognized model architecture.");
        if (!metadata.TryGetValue(name + ".context_length", out var limit)) throw new IOException("The GGUF does not declare its context limit.");
        ContextTokens = Math.Min(settings.ContextTokens, Convert.ToInt32(limit, CultureInfo.InvariantCulture));
        if (ContextTokens < 1024 || settings.OutputTokens + 512 >= ContextTokens) throw new IOException("The output limit leaves too little room for source text. Reduce output tokens or use a larger supported context.");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        process = processes.Start(processes.Tool("llama", "llama-server.exe"), ["-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture), "--api-key", key, "--offline", "--no-webui", "--log-disable", "-ngl", "0", "--no-op-offload", "-c", ContextTokens.ToString(CultureInfo.InvariantCulture), "-t", settings.Threads.ToString(CultureInfo.InvariantCulture), "-b", settings.BatchSize.ToString(CultureInfo.InvariantCulture), "-ub", Math.Min(128, settings.BatchSize).ToString(CultureInfo.InvariantCulture), "--parallel", "1", "--reasoning-format", "deepseek", "--reasoning", "off", "--reasoning-budget", "0"]);
        // Drain without persisting bodies, prompts, paths or native model diagnostics.
        stdout = Drain(process.StandardOutput); stderr = Drain(process.StandardError);
        client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }
    private static async Task<string> Drain(StreamReader stream) { while (await stream.ReadLineAsync() != null) { } return ""; }
    public static async Task<LocalLlm> Start(Processes processes, string modelPath, SummarySettings settings, CancellationToken token)
    {
        if (!Hardware.Compatible) throw new IOException("The CPU runtime requires Windows x64 with AVX2 support.");
        if (settings.ContextTokens is < 1024 or > 8192 || settings.OutputTokens is < 64 or > 2048 || settings.Threads is < 1 or > 64 || settings.BatchSize is < 16 or > 512 || !double.IsFinite(settings.Temperature) || settings.Temperature is < 0 or > 2 || settings.TopP is <= 0 or > 1 || settings.TopK is < 1 or > 200 || settings.RepeatPenalty is < 1 or > 2) throw new IOException("Advanced summary settings are outside the supported ranges.");
        if (Hardware.Memory().Free < new FileInfo(modelPath).Length + 2000L * 1048576) throw new IOException("Not enough free RAM for this model. Choose a smaller model or close other apps.");
        var server = new LocalLlm(processes, modelPath, settings);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested(); if (server.process.HasExited) throw new IOException("The local model could not load. Verify its architecture and use a smaller model if memory is low.");
                try { using var response = await server.client.GetAsync("health", timeout.Token); if (response.IsSuccessStatusCode) break; } catch (HttpRequestException) { }
                await Task.Delay(250, timeout.Token);
            }
            return server;
        }
        catch { await server.DisposeAsync(); throw; }
    }
    public async Task<JsonElement> Post(string path, object body, CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync(path, body, JsonFiles.Options, token);
        if (!response.IsSuccessStatusCode) throw new IOException($"The local inference request failed ({(int)response.StatusCode}). Reduce context/output settings or use a supported GGUF model.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return json.RootElement.Clone();
    }
    public async Task<int> Count(object messages, CancellationToken token)
    {
        var template = await Post("apply-template", new { messages }, token);
        var tokens = await Post("tokenize", new { content = template.GetProperty("prompt").GetString(), add_special = true }, token);
        return tokens.GetProperty("tokens").GetArrayLength();
    }
    public async Task<string> Generate(object messages, SummarySettings settings, List<string> warnings, CancellationToken token)
    {
        var result = await Post("v1/chat/completions", new { messages, max_tokens = settings.OutputTokens, temperature = settings.Temperature, top_p = settings.TopP, top_k = settings.TopK, repeat_penalty = settings.RepeatPenalty, seed = settings.Seed, stream = false, cache_prompt = false }, token);
        var choice = result.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() == "length") warnings.Add("A generation reached the output-token limit. Review for incomplete notes or increase the output limit.");
        var text = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
        text = Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
        if (text.Contains("<think>", StringComparison.OrdinalIgnoreCase)) throw new IOException("The model returned only unfinished reasoning. Choose the Instruct model or increase the output budget.");
        if (string.IsNullOrWhiteSpace(text)) throw new IOException("The model returned no summary. Try Instruct or increase the output budget.");
        return text;
    }
    public async ValueTask DisposeAsync()
    {
        client.Dispose(); try { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); } finally { process.Dispose(); }
    }
}

public sealed class Summarizer(ModelManager models, Processes processes, Pipeline pipeline)
{
    public async Task<Summary> Generate(Job job, Transcript transcript, Model model, SummarySettings settings, Action<string> progress, CancellationToken token)
    {
        if (transcript.Segments.Count == 0) throw new IOException("The selected transcript has no speech to summarize.");
        await pipeline.InferenceGate.WaitAsync(token);
        try
        {
            progress("Verifying summary model"); await models.Verify(model, token);
            progress("Loading local CPU model"); await using var server = await LocalLlm.Start(processes, models.Weights(model), settings, token);
            var warnings = new List<string>();
            object Messages(string source, bool intermediate) => new[] { new { role = "system", content = settings.SystemPrompt }, new { role = "user", content = $"Output language: {settings.Language}. Structure: {(intermediate ? "Grounded chronological notes, preserving source IDs, numbers, actions and uncertainty" : settings.Template == "Custom template" ? settings.CustomTemplate : settings.Template)}. Do not add empty headings.\n<untrusted_context>\n{settings.Context}\n</untrusted_context>\n<untrusted_transcript>\n{source}\n</untrusted_transcript>" } };
            var budget = server.ContextTokens - settings.OutputTokens - 64;
            if (await server.Count(Messages("", true), token) >= budget - 128) throw new IOException("Context/terminology is too long for this model window. Shorten it or raise the context within the supported limit.");
            async Task<List<string>> Pack(List<string> pieces, bool intermediate)
            {
                var groups = new List<string>(); var current = "";
                var pending = new Queue<string>(pieces);
                while (pending.TryDequeue(out var piece))
                {
                    token.ThrowIfCancellationRequested(); var combined = current == "" ? piece : current + "\n" + piece;
                    if (await server.Count(Messages(combined, intermediate), token) <= budget) { current = combined; continue; }
                    if (current != "") { groups.Add(current); current = ""; }
                    if (await server.Count(Messages(piece, intermediate), token) <= budget) { current = piece; continue; }
                    if (piece.Length < 8) throw new IOException("The prompt cannot fit inside the selected context.");
                    var midpoint = piece.Length / 2; var split = piece.LastIndexOf(' ', midpoint);
                    if (split < piece.Length / 4) split = midpoint;
                    if (char.IsHighSurrogate(piece[split - 1])) split--;
                    var remaining = pending.ToList(); pending.Clear(); pending.Enqueue(piece[..split]); pending.Enqueue(piece[split..]); foreach (var item in remaining) pending.Enqueue(item);
                }
                if (current != "") groups.Add(current); return groups;
            }
            var pieces = transcript.Segments.Select(s => $"[{s.SegmentId}] [{s.StartTime}] {(s.SpeakerId ?? "Unknown speaker")}: {s.Text}").ToList();
            var full = string.Join("\n", pieces); string text;
            if (await server.Count(Messages(full, false), token) <= budget)
            { progress("Generating summary in one pass"); text = await server.Generate(Messages(full, false), settings, warnings, token); }
            else
            {
                warnings.Add("The transcript required chunked summarization. Intermediate notes and source references are preserved; review for omissions.");
                var run = Path.Combine(job.Directory, "summary-checkpoints", JsonFiles.Hash(new { Transcript = JsonFiles.Hash(transcript), Model = model.Id, model.Revision, settings }));
                var pass = 0;
                while (true)
                {
                    var groups = await Pack(pieces, true); var next = new List<string>();
                    if (pass > 8) throw new IOException("The summary could not converge within eight reduction passes. Use a larger context or shorter output template. Intermediate notes are saved.");
                    for (var i = 0; i < groups.Count; i++)
                    {
                        progress($"Summary pass {pass + 1}: {i + 1} of {groups.Count}");
                        var checkpoint = Path.Combine(run, $"pass-{pass:00}-chunk-{i:0000}.json");
                        var sourceHash = JsonFiles.Hash(groups[i]);
                        string notes;
                        if (File.Exists(checkpoint)) { var saved = JsonFiles.Read<SummaryCheckpoint>(checkpoint); if (saved.Hash != sourceHash) throw new IOException("Summary checkpoint changed; restart generation."); notes = saved.Text; }
                        else { notes = await server.Generate(Messages(groups[i], true), settings, warnings, token); JsonFiles.Write(checkpoint, new SummaryCheckpoint(sourceHash, notes)); }
                        next.Add(notes);
                    }
                    full = string.Join("\n", next);
                    if (await server.Count(Messages(full, false), token) <= budget) { progress("Consolidating final summary"); text = await server.Generate(Messages(full, false), settings, warnings, token); break; }
                    pieces = next; pass++;
                }
            }
            var ids = transcript.Segments.Select(s => s.SegmentId).ToHashSet();
            text = Regex.Replace(text, @"\[(s\d{6})\]", match => { if (ids.Contains(match.Groups[1].Value)) return match.Value; warnings.Add("An unsupported source reference was removed."); return "[unverified reference]"; });
            var result = new Summary { ModelId = model.Id, ModelRevision = model.Revision, ModelSha256 = model.Artifacts.First(a => a.Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).Sha256, ActualContextTokens = server.ContextTokens, TranscriptHash = JsonFiles.Hash(transcript), TranscriptVersion = transcript.Version, Settings = settings, Text = text, Warnings = warnings.Distinct().ToList() };
            JsonFiles.Write(Path.Combine(job.Directory, "summaries", result.Id, "summary.generated.json"), result);
            JsonFiles.Write(Path.Combine(job.Directory, "summary.json"), result); return result;
        }
        finally { pipeline.InferenceGate.Release(); }
    }
    private sealed record SummaryCheckpoint(string Hash, string Text);
}

public static class Gguf
{
    public static Dictionary<string, object?> Read(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3)) throw new IOException("Choose a supported GGUF v2/v3 text model.");
        var tensors = reader.ReadUInt64(); var count = reader.ReadUInt64(); if (tensors > 1_000_000 || count > 100_000) throw new IOException("GGUF metadata exceeds the supported safety limit.");
        var result = new Dictionary<string, object?>();
        string ReadString() { var size = reader.ReadUInt64(); if (size > 32 * 1024 * 1024 || size > (ulong)(stream.Length - stream.Position)) throw new IOException("Invalid GGUF string metadata."); var bytes = reader.ReadBytes((int)size); return System.Text.Encoding.UTF8.GetString(bytes); }
        object? Value(uint type, int depth = 0)
        {
            if (depth > 2 || stream.Position > 128 * 1024 * 1024) throw new IOException("GGUF metadata is too large.");
            switch (type)
            {
                case 0: return reader.ReadByte(); case 1: return reader.ReadSByte(); case 2: return reader.ReadUInt16(); case 3: return reader.ReadInt16(); case 4: return reader.ReadUInt32(); case 5: return reader.ReadInt32(); case 6: return reader.ReadSingle(); case 7: return reader.ReadBoolean(); case 8: return ReadString(); case 10: return reader.ReadUInt64(); case 11: return reader.ReadInt64(); case 12: return reader.ReadDouble();
                case 9: var inner = reader.ReadUInt32(); var length = reader.ReadUInt64(); if (length > 2_000_000) throw new IOException("Invalid GGUF array metadata."); for (ulong i = 0; i < length; i++) Value(inner, depth + 1); return null;
                default: throw new IOException("Unknown GGUF metadata type.");
            }
        }
        for (ulong i = 0; i < count; i++) { var key = ReadString(); var value = Value(reader.ReadUInt32()); if (key is "general.architecture" or "general.name" || key.EndsWith(".context_length", StringComparison.Ordinal)) result[key] = value; }
        return result;
    }
}
