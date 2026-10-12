using System.IO;
using System.Runtime.InteropServices;

namespace LocalTranscriber;

public sealed record MicrophoneDevice(int Id, string Name) { public override string ToString() => Name; }

// Poll returned native buffers; no unmanaged callback can race disposal or call the UI.
public sealed class Microphone : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct Format { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Caps { public ushort Manufacturer, Product; public uint Driver; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Formats; public ushort Channels, Reserved; }
    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint waveInGetDevCapsW(UIntPtr id, out Caps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);
    private readonly List<IntPtr> buffers = [];
    private IntPtr handle;
    private BinaryWriter? writer;
    private int sampleRate;
    private long bytes;
    private long headerBytes;
    public string Path { get; }
    public double Seconds => bytes / (sampleRate * 2d);
    public double Peak { get; private set; }
    public double Level { get; private set; }
    public static List<MicrophoneDevice> Devices()
    {
        var devices = new List<MicrophoneDevice> { new(-1, "Windows default microphone") };
        for (uint i = 0; i < waveInGetNumDevs(); i++) if (waveInGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<Caps>()) == 0) devices.Add(new((int)i, caps.Name));
        return devices;
    }
    private static void Check(uint code)
    {
        if (code != 0) throw new IOException($"Microphone could not be used (Windows audio error {code}). Check the selected input, Windows Settings → Privacy & security → Microphone → desktop app access, and whether another app has exclusive access.");
    }
    public Microphone(string path, int device)
    {
        Path = path; Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        try
        {
            uint code = 0;
            foreach (var rate in new[] { 16000, 48000, 44100 })
            {
                var format = new Format { Tag = 1, Channels = 1, Rate = (uint)rate, BytesPerSecond = (uint)rate * 2, BlockAlign = 2, Bits = 16 };
                code = waveInOpen(out handle, unchecked((uint)device), ref format, IntPtr.Zero, IntPtr.Zero, 0);
                if (code == 0) { sampleRate = rate; break; }
            }
            Check(code);
            writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            writer.Write(new byte[44]);
            WriteHeader();
            for (var i = 0; i < 6; i++)
            {
                var header = new Header { Data = Marshal.AllocHGlobal(sampleRate / 5), Length = (uint)(sampleRate / 5) };
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Header>()); Marshal.StructureToPtr(header, pointer, false); buffers.Add(pointer);
                Check(waveInPrepareHeader(handle, pointer, (uint)Marshal.SizeOf<Header>())); Check(waveInAddBuffer(handle, pointer, (uint)Marshal.SizeOf<Header>()));
            }
            Check(waveInStart(handle));
        }
        catch { Dispose(); throw; }
    }
    public static double Rms(byte[] pcm)
    {
        double energy = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2) { var sample = (short)(pcm[i] | pcm[i + 1] << 8) / 32768d; energy += sample * sample; }
        return pcm.Length < 2 ? 0 : Math.Sqrt(energy / (pcm.Length / 2));
    }
    public void Poll(bool requeue = true)
    {
        if (handle == IntPtr.Zero || writer == null) return;
        Level *= .8;
        foreach (var pointer in buffers)
        {
            var header = Marshal.PtrToStructure<Header>(pointer);
            if ((header.Flags & 1) == 0) continue;
            if (header.Recorded > 0)
            {
                if (bytes + header.Recorded > uint.MaxValue - 36) throw new IOException("The recording reached WAV's size limit. Stop and start a new recording.");
                var data = new byte[header.Recorded]; Marshal.Copy(header.Data, data, 0, data.Length); writer.Write(data); bytes += data.Length;
                var rms = Rms(data); Peak = Math.Max(Peak, rms); Level = Math.Max(Level, rms);
            }
            if (requeue) Check(waveInAddBuffer(handle, pointer, (uint)Marshal.SizeOf<Header>()));
        }
        // Keep a recoverable WAV if Windows or the app exits without normal shutdown.
        if (bytes - headerBytes >= sampleRate * 2) WriteHeader();
    }
    private void WriteHeader()
    {
        if (writer == null) return;
        writer.Seek(0, SeekOrigin.Begin); writer.Write("RIFF"u8); writer.Write((uint)(36 + bytes)); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((ushort)2); writer.Write((ushort)16); writer.Write("data"u8); writer.Write((uint)bytes);
        writer.Seek(0, SeekOrigin.End); writer.Flush(); headerBytes = bytes;
    }
    public void Dispose()
    {
        try
        {
            if (handle != IntPtr.Zero)
            {
                waveInReset(handle);
                try { Poll(false); }
                finally
                {
                    foreach (var pointer in buffers) { waveInUnprepareHeader(handle, pointer, (uint)Marshal.SizeOf<Header>()); var header = Marshal.PtrToStructure<Header>(pointer); Marshal.FreeHGlobal(header.Data); Marshal.FreeHGlobal(pointer); }
                    buffers.Clear(); waveInClose(handle); handle = IntPtr.Zero;
                }
            }
        }
        finally
        {
            if (writer != null)
            {
                try { WriteHeader(); }
                finally { writer.Dispose(); writer = null; }
            }
        }
    }
}
