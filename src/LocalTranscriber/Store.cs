using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalTranscriber;

public sealed class Store : IDisposable
{
    public static string DefaultRoot => Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalTranscriber");
    public string Root { get; }
    private readonly SqliteConnection database;
    private readonly object sync = new();
    public Settings Settings { get; }
    public Store(string? root = null)
    {
        Root = root ?? DefaultRoot;
        Directory.CreateDirectory(Root);
        Settings = File.Exists(Path.Combine(Root, "settings.json")) ? JsonFiles.Read<Settings>(Path.Combine(Root, "settings.json")) : new Settings { ModelRoot = Path.Combine(Root, "models"), CacheRoot = Path.Combine(Root, "cache") };
        Directory.CreateDirectory(Settings.ModelRoot); Directory.CreateDirectory(Settings.CacheRoot);
        database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "library.sqlite"), Pooling = false }.ToString());
        database.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        Execute("CREATE TABLE IF NOT EXISTS workspaces(id TEXT PRIMARY KEY,path TEXT UNIQUE NOT NULL,label TEXT NOT NULL,position INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS media(id TEXT PRIMARY KEY,workspace_id TEXT NOT NULL,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY,workspace_id TEXT NOT NULL,json TEXT NOT NULL); PRAGMA user_version=1;");
        foreach (var job in Jobs().Where(j => j.State is not (JobState.Completed or JobState.Canceled or JobState.Failed or JobState.Queued)))
        { job.State = JobState.Interrupted; job.Stage = "Interrupted — resume or restart"; SaveJob(job); }
    }
    private void Execute(string sql, params (string Name, object Value)[] values)
    {
        lock (sync) { using var command = database.CreateCommand(); command.CommandText = sql; foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); command.ExecuteNonQuery(); }
    }
    public List<Workspace> Workspaces()
    {
        lock (sync) { using var command = database.CreateCommand(); command.CommandText = "SELECT id,path,label,position FROM workspaces ORDER BY position"; using var reader = command.ExecuteReader(); var list = new List<Workspace>(); while (reader.Read()) list.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3))); return list; }
    }
    public Workspace AddWorkspace(string path)
    {
        path = Path.GetFullPath(path); if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Choose an accessible workspace folder.");
        var existing = Workspaces().FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)); if (existing != null) return existing;
        var workspace = new Workspace(Guid.NewGuid().ToString("N"), path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), Workspaces().Count);
        Execute("INSERT INTO workspaces VALUES($id,$path,$label,$position)", ("$id", workspace.Id), ("$path", path), ("$label", workspace.Label), ("$position", workspace.Position)); return workspace;
    }
    public void UpdateWorkspace(Workspace workspace) => Execute("UPDATE workspaces SET path=$path,label=$label,position=$position WHERE id=$id", ("$id", workspace.Id), ("$path", workspace.Path), ("$label", workspace.Label), ("$position", workspace.Position));
    public void RemovePointer(string id) => Execute("DELETE FROM workspaces WHERE id=$id", ("$id", id));
    public void SaveSettings() => JsonFiles.Write(Path.Combine(Root, "settings.json"), Settings);
    private List<T> ReadRows<T>(string table, string? workspace)
    {
        if (table is not ("media" or "jobs")) throw new ArgumentException("Invalid index table.");
        lock (sync) { using var command = database.CreateCommand(); command.CommandText = $"SELECT json FROM {table}" + (workspace == null ? "" : " WHERE workspace_id=$workspace"); if (workspace != null) command.Parameters.AddWithValue("$workspace", workspace); using var reader = command.ExecuteReader(); var list = new List<T>(); while (reader.Read()) list.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonFiles.Options)!); return list; }
    }
    public List<Media> Media(string workspace) => ReadRows<Media>("media", workspace);
    public List<Job> Jobs(string? workspace = null) => ReadRows<Job>("jobs", workspace).OrderBy(j => j.CreatedAt).ToList();
    public void SaveMedia(Media media) => Execute("INSERT OR REPLACE INTO media VALUES($id,$workspace,$json)", ("$id", media.Id), ("$workspace", media.WorkspaceId), ("$json", JsonSerializer.Serialize(media, JsonFiles.Options)));
    public void SaveJob(Job job)
    {
        // Manifest first: a failed database write never loses the worker checkpoint.
        JsonFiles.Write(Path.Combine(job.Directory, "job.json"), job);
        Execute("INSERT OR REPLACE INTO jobs VALUES($id,$workspace,$json)", ("$id", job.Id), ("$workspace", job.WorkspaceId), ("$json", JsonSerializer.Serialize(job, JsonFiles.Options)));
    }
    public string OutputRoot(Workspace workspace, out bool fallback)
    {
        try { if (!workspace.Available) throw new IOException(); var dir = Path.Combine(workspace.Path, ".localtranscriber"); Directory.CreateDirectory(dir); var test = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp"); File.WriteAllText(test, ""); File.Delete(test); fallback = false; JsonFiles.Write(Path.Combine(dir, "workspace.json"), workspace); return dir; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { fallback = true; var dir = Path.Combine(Root, "workspaces", workspace.Id); Directory.CreateDirectory(dir); return dir; }
    }
    public static Transcript LoadTranscript(Job job, bool raw = false) => JsonFiles.Read<Transcript>(Path.Combine(job.Directory, !raw && File.Exists(Path.Combine(job.Directory, "transcript.edited.json")) ? "transcript.edited.json" : "transcript.raw.json"));
    public static void SaveEdited(Job job, Transcript transcript)
    {
        transcript.Version = "edited";
        JsonFiles.Write(Path.Combine(job.Directory, "history", $"transcript-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.json"), transcript);
        JsonFiles.Write(Path.Combine(job.Directory, "transcript.edited.json"), transcript);
    }
    public void Dispose() => database.Dispose();
}
