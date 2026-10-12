using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace LocalTranscriber;

public sealed class DictationTarget
{
    [StructLayout(LayoutKind.Sequential)] private struct GuiInfo { public uint Size, Flags; public IntPtr Active, Focus, Capture, Menu, Move, Caret; public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private readonly IntPtr window, focus;
    private readonly AutomationElement element;
    private static DataObject? pendingClipboard;
    private static uint pendingSequence;
    public static bool ClipboardNeedsRestore => pendingClipboard != null;
    public static bool ClipboardWasChanged => pendingClipboard != null && GetClipboardSequenceNumber() != pendingSequence;
    public static void RestoreClipboard(bool overwriteChanged = false)
    {
        if (pendingClipboard == null) return;
        if (ClipboardWasChanged && !overwriteChanged) throw new IOException("The clipboard changed since dictation. Restoring would replace its newer contents.");
        if (pendingClipboard.GetFormats(false).Length == 0) Clipboard.Clear(); else Clipboard.SetDataObject(pendingClipboard, true);
        pendingClipboard = null;
    }
    public int ProcessId => element.Current.ProcessId;
    private DictationTarget(IntPtr window, IntPtr focus, AutomationElement element) { this.window = window; this.focus = focus; this.element = element; }
    private static IntPtr Focus(IntPtr window) { var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() }; return GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info) ? info.Focus : IntPtr.Zero; }
    public static DictationTarget? Capture(out string message)
    {
        try
        {
            var window = GetForegroundWindow(); GetWindowThreadProcessId(window, out var pid);
            if (pid == Environment.ProcessId) throw new IOException("Select a text box in another app, then click Record in the floating widget.");
            var element = AutomationElement.FocusedElement;
            if (!Editable(element)) throw new IOException("Select an editable text box. Password and read-only fields cannot receive dictation.");
            message = "Text box selected · keep it focused until transcription finishes";
            return new(window, Focus(window), element);
        }
        catch (Exception error) { message = error.Message; return null; }
    }
    private static bool Editable(AutomationElement? element)
    {
        if (element == null || !element.Current.IsEnabled || element.Current.IsPassword) return false;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)) return !((ValuePattern)value).Current.IsReadOnly;
        return element.Current.ControlType == ControlType.Edit && element.TryGetCurrentPattern(TextPattern.Pattern, out _);
    }
    private void ValidateFocus()
    {
        if (GetForegroundWindow() != window || Focus(window) != focus || !Automation.Compare(element, AutomationElement.FocusedElement) || !Editable(element)) throw new IOException("The selected text box changed. Your result is saved in Speech-to-text; copy it into the intended field.");
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0)) throw new IOException("A modifier key is held. Release it and copy the saved result from Speech-to-text.");
    }
    private static void Send(Input[] input)
    {
        var sent = SendInput((uint)input.Length, input, Marshal.SizeOf<Input>());
        if (sent != input.Length)
        {
            if (sent > 0 && input.Any(i => i.Keyboard.Key == 0x11)) SendInput(2, [new Input { Type = 1, Keyboard = new() { Key = 0x56, Flags = 2 } }, new Input { Type = 1, Keyboard = new() { Key = 0x11, Flags = 2 } }], Marshal.SizeOf<Input>());
            throw new IOException("Windows blocked all or part of insertion (for example, an elevated app). The result is saved; review the field before copying to avoid duplicates.");
        }
    }
    private string ReadText() => element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) ? ((ValuePattern)value).Current.Value : ((TextPattern)element.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.GetText(-1);
    private async Task<bool> ConfirmText(string text, string original)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(100); var current = ReadText();
            if (current.Replace("\r\n", "\n").Contains(text.Replace("\r\n", "\n")) && current != original) return true;
        }
        return false;
    }
    public async Task Insert(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 64000) throw new IOException("This result is too long for direct insertion. Copy it from the workbench.");
        ValidateFocus();
        // Rapid Unicode packets can drop characters; confirmed paste also preserves paragraphs.
        if (ClipboardNeedsRestore) throw new IOException("Restore the previous clipboard from Speech-to-text before another automatic paste.");
        var originalText = ReadText();
        var sequence = GetClipboardSequenceNumber(); var previous = Clipboard.GetDataObject(); var backup = new DataObject();
        if (previous != null)
            foreach (var format in previous.GetFormats(false))
            {
                var data = previous.GetData(format, false) ?? throw new IOException("The clipboard could not be preserved. Use Copy result after reviewing the transcription.");
                backup.SetData(format, data is MemoryStream stream ? new MemoryStream(stream.ToArray()) : data, false);
            }
        if (GetClipboardSequenceNumber() != sequence) throw new IOException("The clipboard changed. Your result is saved; copy it when ready.");
        uint ownedSequence = 0;
        var sentPaste = false; var confirmed = false;
        try
        {
            Clipboard.SetText(text); ownedSequence = GetClipboardSequenceNumber(); ValidateFocus();
            Send([new Input { Type = 1, Keyboard = new() { Key = 0x11 } }, new Input { Type = 1, Keyboard = new() { Key = 0x56 } }, new Input { Type = 1, Keyboard = new() { Key = 0x56, Flags = 2 } }, new Input { Type = 1, Keyboard = new() { Key = 0x11, Flags = 2 } }]);
            sentPaste = true;
            confirmed = await ConfirmText(text, originalText);
            if (!confirmed) throw new IOException("Paste could not be confirmed. The clipboard still contains your dictation. Review the target, then use Restore previous clipboard in Speech-to-text.");
        }
        finally
        {
            if (ownedSequence != 0 && GetClipboardSequenceNumber() == ownedSequence)
            {
                if (sentPaste && !confirmed) { pendingClipboard = backup; pendingSequence = ownedSequence; }
                else if (previous == null || backup.GetFormats(false).Length == 0) Clipboard.Clear(); else Clipboard.SetDataObject(backup, true);
            }
        }
    }
}
