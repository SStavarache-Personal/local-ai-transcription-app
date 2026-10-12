using LocalTranscriber;
using System.IO;
using System.Security.Cryptography;

if (args.Length is < 2 or > 3) throw new ArgumentException("Supply a report path and isolated test data directory; optionally supply model storage to complete verified cached downloads.");
using var store = new Store(args[1]);
var manager = new ModelManager(store);
var evidence = new List<object>();
var cachedBytes = System.Text.Encoding.UTF8.GetBytes("owned cached weight fixture");
var cachedHash = Convert.ToHexString(SHA256.HashData(cachedBytes)).ToLowerInvariant();
var fixtureArtifact = new Artifact("cached-weights.gguf", "https://not-approved.example/never-fetch", cachedBytes.Length, cachedHash);
foreach (var id in args.Length > 2 ? new[] { "qwen35-0.8b", "qwen35-2b" } : new[] { "qwen35-0.8b", "qwen35-2b", "qwen35-4b", "gemma4-e2b", "gemma4-e4b" })
{
    var original = manager.Catalogue.Single(m => m.Id == id);
    var profile = args.Length > 2 ? original : original with { Id = "license-check-" + id, Artifacts = [fixtureArtifact, original.Artifacts.Single(a => a.Name == "LICENSE")] };
    if (args.Length > 2) store.Settings.ModelRoot = args[2];
    var stage = ModelManager.OwnedPath(store.Settings.ModelRoot, ".staging", profile.Id);
    if (args.Length < 3) { Directory.CreateDirectory(stage); File.WriteAllBytes(ModelManager.OwnedPath(stage, fixtureArtifact.Name), cachedBytes); }
    if (manager.Installed(profile)) { await manager.Verify(profile, CancellationToken.None); evidence.Add(new { Model = id, AlreadyInstalled = true }); continue; }
    var weights = profile.Artifacts.Where(a => a.Name.EndsWith(".gguf")).ToList();
    foreach (var artifact in weights) { var file = ModelManager.OwnedPath(stage, artifact.Name); if (!File.Exists(file) || new FileInfo(file).Length != artifact.Size || await ModelManager.Hash(file, CancellationToken.None) != artifact.Sha256) throw new Exception("Completed cached weights failed verification: " + id); }
    await manager.Download(profile, (_, _) => { }, CancellationToken.None);
    await manager.Verify(profile, CancellationToken.None);
    if (!manager.Installed(profile) || Directory.Exists(stage)) throw new Exception("Installation was not finalized: " + id);
    evidence.Add(new { Model = id, Installed = true, ReusedWeightBytes = weights.Sum(a => a.Size), LicenceBytes = profile.Artifacts.Single(a => a.Name == "LICENSE").Size });
}
if (args.Length < 3)
{
    var source = manager.Catalogue.Single(m => m.Id == "qwen35-0.8b");
    var invalid = source with { Id = "invalid-license-hash", Artifacts = [fixtureArtifact, source.Artifacts.Single(a => a.Name == "LICENSE") with { Sha256 = new string('0', 64) }] };
    var stage = ModelManager.OwnedPath(store.Settings.ModelRoot, ".staging", invalid.Id); Directory.CreateDirectory(stage);
    var fixtureFile = ModelManager.OwnedPath(stage, fixtureArtifact.Name); File.WriteAllBytes(fixtureFile, cachedBytes);
    var rejected = false; try { await manager.Download(invalid, (_, _) => { }, CancellationToken.None); } catch (IOException error) when (error.Message.Contains("integrity check")) { rejected = true; }
    if (!rejected || manager.Installed(invalid) || await ModelManager.Hash(fixtureFile, CancellationToken.None) != cachedHash) throw new Exception("Bad licence hash was accepted or cached weights were lost.");
    evidence.Add(new { Check = "Bad licence hash rejected; completed cached weights preserved", Passed = true });
}
JsonFiles.Write(args[0], new { Result = "Passed", Evidence = evidence });
Console.WriteLine(File.ReadAllText(args[0]));
