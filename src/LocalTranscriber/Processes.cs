using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LocalTranscriber;

public sealed class OwnedProcesses : IDisposable
{
    // Windows kills every assigned worker when the app exits, including after a crash.
    private readonly SafeFileHandle handle;
    public OwnedProcesses()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
        var limits = new ExtendedLimits { BasicLimitInformation = new BasicLimits { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new System.ComponentModel.Win32Exception();
    }
    public void Add(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) { process.Kill(true); throw new System.ComponentModel.Win32Exception(); } }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessUserTimeLimit, JobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}

public sealed class Processes : IDisposable
{
    private readonly OwnedProcesses owned = new();
    public string RuntimeRoot { get; }
    public List<WorkerMetric> Metrics { get; } = [];
    public Processes(string? root = null) => RuntimeRoot = root ?? Path.Combine(AppContext.BaseDirectory, "runtime");
    public string Tool(string runtime, string name)
    {
        var path = Path.Combine(RuntimeRoot, runtime, name);
        if (File.Exists(path)) return path;
        if (Directory.Exists(Path.GetDirectoryName(path))) { var found = Directory.GetFiles(Path.Combine(RuntimeRoot, runtime), name, SearchOption.AllDirectories).FirstOrDefault(); if (found != null) return found; }
        throw new FileNotFoundException($"Bundled {name} is missing. Re-extract the complete portable application.");
    }
    public Process Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        info.Environment["HF_HUB_OFFLINE"] = "1"; info.Environment["TRANSFORMERS_OFFLINE"] = "1"; info.Environment["PYTHONUTF8"] = "1";
        info.Environment["CUDA_VISIBLE_DEVICES"] = "";
        var process = Process.Start(info) ?? throw new IOException("Could not start the local worker."); owned.Add(process); return process;
    }
    public async Task<(string Output, string Error)> Run(string executable, IEnumerable<string> args, CancellationToken token, Action<string>? line = null)
    {
        var watch = Stopwatch.StartNew();
        using var process = Start(executable, args);
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        async Task<string> Drain(StreamReader reader, bool notify)
        {
            var text = new StringBuilder();
            while (await reader.ReadLineAsync() is { } current) { if (notify) line?.Invoke(current); if (text.Length < 2_000_000) text.AppendLine(current); }
            return text.ToString();
        }
        var stdout = Drain(process.StandardOutput, true); var stderr = Drain(process.StandardError, false);
        long peak = 0; var exited = process.WaitForExitAsync(CancellationToken.None);
        while (!exited.IsCompleted) { try { process.Refresh(); peak = Math.Max(peak, process.PeakWorkingSet64); } catch (InvalidOperationException) { } await Task.WhenAny(exited, Task.Delay(100)); }
        await exited; var output = await stdout; var error = await stderr;
        watch.Stop();
        try { lock (Metrics) { if (Metrics.Count >= 500) Metrics.RemoveAt(0); Metrics.Add(new WorkerMetric(Path.GetFileName(executable), watch.Elapsed.TotalSeconds, peak, process.TotalProcessorTime.TotalSeconds)); } } catch (InvalidOperationException) { }
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException($"Local worker exited with code {process.ExitCode}. " + SafeError(error));
        return (output, error);
    }
    private static string SafeError(string stderr)
    {
        // Keep prompts, transcript bodies and user paths out of persistent diagnostics.
        if (stderr.Contains("memory", StringComparison.OrdinalIgnoreCase)) return "Try a smaller model or context and close other applications.";
        if (stderr.Contains("Invalid", StringComparison.OrdinalIgnoreCase)) return "The media or model may be invalid. Try a verified model or another audio track.";
        return "Check that the complete runtime and verified model files are present.";
    }
    public void Dispose() => owned.Dispose();
}
public sealed record WorkerMetric(string Tool, double WallSeconds, long PeakWorkingSetBytes, double CpuSeconds);
