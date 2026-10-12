using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LocalTranscriber;

public sealed partial class ModelManager(Store store)
{
    public List<Model> Catalogue { get; private set; } = JsonFiles.Read<List<Model>>(Path.Combine(AppContext.BaseDirectory, "catalogue.json"));
    private readonly SemaphoreSlim mutations = new(1);
    public void LoadImports() { var path = Path.Combine(store.Root, "imported-models.json"); if (File.Exists(path)) Catalogue.AddRange(JsonFiles.Read<List<Model>>(path)); }
    private string ModelDirectory(Model model) => OwnedPath(store.Settings.ModelRoot, model.Role, model.Id);
    public string Location(Model model) => ModelDirectory(model);
    public bool Installed(Model model)
    {
        try { var dir = ModelDirectory(model); return File.Exists(Path.Combine(dir, "installed.json")) && model.Artifacts.Count > 0 && model.Artifacts.All(a => File.Exists(OwnedPath(dir, a.Name)) && new FileInfo(OwnedPath(dir, a.Name)).Length == a.Size); }
        catch (IOException) { return false; }
    }
    public string Weights(Model model) => OwnedPath(ModelDirectory(model), model.Artifacts.First(a => a.Name.EndsWith(model.Runtime == "llama.cpp" ? ".gguf" : ".bin", StringComparison.OrdinalIgnoreCase)).Name);
    public static string OwnedPath(string root, params string[] parts)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(new[] { fullRoot }.Concat(parts).ToArray()));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("The path leaves the managed folder.");
        for (var current = result; current != null && current.Length >= fullRoot.TrimEnd(Path.DirectorySeparatorChar).Length; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Managed storage cannot use symlinks or junctions.");
        return result;
    }
    public async Task Verify(Model model, CancellationToken token)
    {
        if (!Installed(model)) throw new IOException("Choose or download a compatible model in Models.");
        foreach (var file in model.Artifacts) { token.ThrowIfCancellationRequested(); var path = OwnedPath(ModelDirectory(model), file.Name); if (await Hash(path, token) != file.Sha256) throw new IOException("Model integrity check failed. Remove and download it again."); }
    }
    public static async Task<string> Hash(string path, CancellationToken token)
    { await using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant(); }
    public async Task Download(Model model, Action<long, long> progress, CancellationToken token)
    {
        if (!model.Available || model.Artifacts.Count == 0) throw new IOException(model.Blocker ?? "This model is unavailable.");
        await mutations.WaitAsync(token);
        try
        {
            if (Installed(model)) return;
            var stage = OwnedPath(store.Settings.ModelRoot, ".staging", model.Id);
            Directory.CreateDirectory(stage);
            var required = model.DownloadSize + 256L * 1048576;
            if (new DriveInfo(Path.GetPathRoot(stage)!).AvailableFreeSpace < required) throw new IOException($"Not enough disk space. Keep at least {required / 1048576:N0} MB free, or move model storage in Settings.");
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            long completed = 0;
            foreach (var artifact in model.Artifacts)
            {
                if (artifact.Size <= 0 || !ShaRegex().IsMatch(artifact.Sha256)) throw new IOException("Invalid catalogue artifact.");
                var target = OwnedPath(stage, artifact.Name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target) && new FileInfo(target).Length == artifact.Size && await Hash(target, token) == artifact.Sha256) { completed += artifact.Size; progress(completed, model.DownloadSize); continue; }
                var part = target + ".partial";
                var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (offset > artifact.Size) { File.Delete(part); offset = 0; }
                var uri = new Uri(artifact.Url);
                using var response = await Get(client, uri, offset, token);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset == artifact.Size) { }
                else
                {
                    response.EnsureSuccessStatusCode();
                    var append = response.StatusCode == HttpStatusCode.PartialContent;
                    if (append && response.Content.Headers.ContentRange?.From != offset) throw new IOException("The server returned an invalid download range.");
                    if (!append) offset = 0;
                    await using var destination = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true);
                    await using var source = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[1048576]; int read;
                    while ((read = await source.ReadAsync(buffer, token)) > 0) { if (offset + read > artifact.Size) throw new IOException("Download exceeds the pinned size."); await destination.WriteAsync(buffer.AsMemory(0, read), token); offset += read; progress(completed + offset, model.DownloadSize); }
                }
                if (new FileInfo(part).Length != artifact.Size || await Hash(part, token) != artifact.Sha256) { File.Delete(part); throw new IOException("Model download failed its integrity check. Retry the download."); }
                File.Move(part, target, true); completed += artifact.Size;
            }
            JsonFiles.Write(Path.Combine(stage, "installed.json"), model);
            var final = ModelDirectory(model); Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            if (Directory.Exists(final)) throw new IOException("An incomplete model directory already exists. Remove it before retrying.");
            Directory.Move(stage, final);
        }
        finally { mutations.Release(); }
    }
    internal static bool AllowedDownloadUri(Uri uri) => uri.Scheme == "https" && (uri.Host == "huggingface.co" || uri.Host == "hf.co" || uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) || (uri.Host == "www.apache.org" && uri.AbsolutePath == "/licenses/LICENSE-2.0.txt"));
    private static async Task<HttpResponseMessage> Get(HttpClient client, Uri uri, long offset, CancellationToken token)
    {
        for (var redirects = 0; redirects < 8; redirects++)
        {
            if (!AllowedDownloadUri(uri)) throw new IOException("The download URL or redirect is outside the approved model and licence sources.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location) { uri = location.IsAbsoluteUri ? location : new Uri(uri, location); response.Dispose(); continue; }
            return response;
        }
        throw new IOException("Too many model download redirects.");
    }
    public async Task<long> Delete(Model model, CancellationToken token)
    {
        await mutations.WaitAsync(token);
        try { var dir = ModelDirectory(model); if (!Directory.Exists(dir)) return 0; var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); foreach (var file in files) OwnedPath(dir, Path.GetRelativePath(dir, file)); foreach (var subdir in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)) OwnedPath(dir, Path.GetRelativePath(dir, subdir)); var size = files.Sum(f => new FileInfo(f).Length); Directory.Delete(dir, true); return size; }
        finally { mutations.Release(); }
    }
    public async Task<Model> Import(string source, Model profile, Func<Model, string, CancellationToken, Task> validate, CancellationToken token)
    {
        await mutations.WaitAsync(token);
        var model = profile with { Id = "imported-" + Guid.NewGuid().ToString("N"), Name = Path.GetFileName(source), Revision = "local-import", Source = "Local import", License = "User-supplied; verify the original model licence", Artifacts = [] };
        var dir = ModelDirectory(model);
        try
        {
            Directory.CreateDirectory(dir);
            IEnumerable<string> files = Directory.Exists(source) ? profile.Artifacts.Select(a => Path.Combine(source, a.Name)) : new[] { source };
            foreach (var file in files)
            {
                if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Required compatible model files are missing, or the source uses a link.");
                var name = Path.GetFileName(file); var target = OwnedPath(dir, name);
                await using (var input = File.OpenRead(file)) await using (var output = File.Create(target)) await input.CopyToAsync(output, token);
                model.Artifacts.Add(new(name, "", new FileInfo(target).Length, await Hash(target, token)));
            }
            var known = profile.Artifacts.Count == model.Artifacts.Count && model.Artifacts.All(a => profile.Artifacts.Any(p => p.Name == a.Name && p.Sha256 == a.Sha256));
            if (!known) model = model with { Languages = [], ForcedLanguage = false, EstimatedMemoryMb = Math.Max(profile.EstimatedMemoryMb, model.DownloadSize / 1048576 * 2 + 1024), Description = "User-supplied weights. CPU compatibility is validated below; language support is unverified and RAM is a conservative estimate. Speech imports use automatic language selection." };
            await validate(model, dir, token); // A recognized header alone is not compatibility proof.
            JsonFiles.Write(Path.Combine(dir, "installed.json"), model);
            Catalogue.Add(model); JsonFiles.Write(Path.Combine(store.Root, "imported-models.json"), Catalogue.Where(m => m.Id.StartsWith("imported-"))); return model;
        }
        catch { if (Directory.Exists(dir)) Directory.Delete(dir, true); throw; }
        finally { mutations.Release(); }
    }
    public async Task MoveStorage(string destination, Action<long, long> progress, CancellationToken token)
    {
        await mutations.WaitAsync(token);
        try
        {
            destination = Path.GetFullPath(destination); var original = Path.GetFullPath(store.Settings.ModelRoot);
            if (destination.StartsWith(original + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || original.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.Equals(original, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a separate model storage folder.");
            Directory.CreateDirectory(destination);
            if (Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Choose an empty destination folder.");
            OwnedPath(destination, "validation");
            var files = Directory.GetFiles(original, "*", SearchOption.AllDirectories); var total = files.Sum(f => new FileInfo(f).Length); long copied = 0;
            if (new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace < total + 256L * 1048576) throw new IOException("Not enough free disk space in the destination.");
            try
            {
                foreach (var file in files) { token.ThrowIfCancellationRequested(); var relative = Path.GetRelativePath(original, file); OwnedPath(original, relative); var target = OwnedPath(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using (var input = File.OpenRead(file)) await using (var output = File.Create(target)) await input.CopyToAsync(output, token); if (await Hash(file, token) != await Hash(target, token)) throw new IOException("Model move verification failed."); copied += new FileInfo(file).Length; progress(copied, total); }
                store.Settings.ModelRoot = destination; store.SaveSettings();
            }
            catch { foreach (var file in Directory.GetFiles(destination, "*", SearchOption.AllDirectories)) { OwnedPath(destination, Path.GetRelativePath(destination, file)); File.Delete(file); } throw; }
            // Retain the old copy: deletion requires a separate, scope-specific user confirmation.
        }
        finally { mutations.Release(); }
    }
    [GeneratedRegex("^[a-f0-9]{64}$")] private static partial Regex ShaRegex();
}
