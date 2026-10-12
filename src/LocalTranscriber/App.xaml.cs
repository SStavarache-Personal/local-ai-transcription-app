using System.IO;
using System.Windows;

namespace LocalTranscriber;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--input-test-host")) { DictationChecks.ShowInputHost(e.Args); return; }
        if (e.Args.Contains("--self-test") || e.Args.Contains("--smoke"))
        {
            try { var result = await Checks.Run(e.Args); File.WriteAllText(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT") ?? Path.Combine(AppContext.BaseDirectory, "test-results.json"), result); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT") ?? Path.Combine(AppContext.BaseDirectory, "test-results.json"), error.ToString()); Shutdown(1); }
            return;
        }
        DispatcherUnhandledException += (_, args) => { if (e.Args.Contains("--ui-check")) { File.WriteAllText(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT")!, args.Exception.ToString()); args.Handled = true; Shutdown(1); return; } MessageBox.Show("The operation failed. Your original media and saved results are preserved.\n\n" + args.Exception.Message, "Local Transcriber", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        try { var window = new MainWindow(); MainWindow = window; window.Show(); if (e.Args.Contains("--ui-check")) { try { if (e.Args.Contains("--dictation-check")) await window.CheckDictation(e.Args); else await window.CheckUi(e.Args); Shutdown(0); } catch (Exception error) { File.WriteAllText(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT") ?? Path.Combine(AppContext.BaseDirectory, "ui-results.json"), error.ToString()); Shutdown(1); } } }
        catch (Exception error) { if (e.Args.Contains("--ui-check")) File.WriteAllText(Environment.GetEnvironmentVariable("LOCALTRANSCRIBER_REPORT")!, error.ToString()); else MessageBox.Show("Local Transcriber could not start. Re-extract the entire portable app into a writable folder.\n\n" + error.Message); Shutdown(1); }
    }
}
