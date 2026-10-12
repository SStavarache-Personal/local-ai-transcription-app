using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;

namespace LocalTranscriber;
public partial class DictationWidget : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    public DictationWidget()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, (IntPtr)(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000));
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) => { if (message == 0x21) { handled = true; return (IntPtr)3; } return IntPtr.Zero; });
        };
    }
    public void SetState(bool recording, bool processing, bool result)
    {
        RecordButton.IsEnabled = !recording && !processing; StopButton.IsEnabled = recording;
        CancelButton.IsEnabled = recording || processing; RewriteToggle.IsEnabled = !recording && !processing;
        CopyButton.IsEnabled = result && !recording && !processing; WorkbenchButton.IsEnabled = !recording && !processing;
    }
    public void SetLevel(double rms, double seconds)
    {
        VolumeCircle.Width = VolumeCircle.Height = 22 + Math.Clamp(Math.Sqrt(rms) * 160, 0, 48);
        TimerText.Text = TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
        AutomationProperties.SetHelpText(VolumeCircle, $"Microphone level {rms * 100:N1} percent");
    }
}
