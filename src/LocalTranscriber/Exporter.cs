using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LocalTranscriber;

public enum TimestampStyle { None, Start, StartEnd }
public static class Exporter
{
    public static string Time(long milliseconds, char separator)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{separator}{time.Milliseconds:000}";
    }
    public static string Transcript(Transcript transcript, string format, TimestampStyle timestamps = TimestampStyle.StartEnd, bool speakers = true)
    {
        string Speaker(Segment s) => speakers && !string.IsNullOrWhiteSpace(s.SpeakerId) ? s.SpeakerId + ": " : "";
        string Prefix(Segment s) => timestamps == TimestampStyle.None ? "" : "[" + Time(s.StartMs, '.') + (timestamps == TimestampStyle.StartEnd ? " – " + Time(s.EndMs, '.') : "") + "] ";
        var rows = transcript.Segments;
        if (format == "json") return JsonSerializer.Serialize(transcript, JsonFiles.Options);
        if (format == "txt") return string.Join("\n", rows.Select(s => Prefix(s) + Speaker(s) + s.Text)) + "\n";
        if (format == "md") return string.Join("\n\n", rows.Select(s => (timestamps == TimestampStyle.None ? "" : "**" + Prefix(s).Trim() + "** ") + Speaker(s) + s.Text)) + "\n";
        if (format == "csv")
        {
            static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            return "segment_id,start_ms,end_ms,start_time,end_time,speaker,text,language,confidence\r\n" + string.Join("\r\n", rows.Select(s => string.Join(",", new[] { s.SegmentId, timestamps == TimestampStyle.None ? "" : s.StartMs.ToString(CultureInfo.InvariantCulture), timestamps == TimestampStyle.None ? "" : s.EndMs.ToString(CultureInfo.InvariantCulture), timestamps == TimestampStyle.None ? "" : Time(s.StartMs, '.'), timestamps == TimestampStyle.None ? "" : Time(s.EndMs, '.'), speakers ? s.SpeakerId ?? "" : "", s.Text, s.Language ?? "", s.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "" }.Select(Quote)))) + "\r\n";
        }
        if (format is "srt" or "vtt")
        {
            var output = new StringBuilder(format == "vtt" ? "WEBVTT\n\n" : "");
            long end = 0; var index = 0;
            foreach (var s in rows.OrderBy(s => s.StartMs))
            {
                if (s.StartMs < 0 || s.EndMs <= s.StartMs || s.EndMs > transcript.DurationMs + 1000) throw new InvalidDataException("A segment has invalid timing. Correct it before subtitle export.");
                var start = Math.Max(end, s.StartMs); if (start >= s.EndMs) continue;
                end = s.EndMs; index++;
                if (format == "srt") output.AppendLine(index.ToString(CultureInfo.InvariantCulture));
                output.Append(Time(start, format == "srt" ? ',' : '.')).Append(" --> ").AppendLine(Time(end, format == "srt" ? ',' : '.'));
                var text = Speaker(s) + s.Text.Replace("-->", "→").Replace('\r', ' ').Replace('\n', ' ');
                if (format == "vtt") text = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                output.AppendLine(Wrap(text, 48)).AppendLine();
            }
            return output.ToString();
        }
        throw new ArgumentException("Choose TXT, Markdown, JSON, CSV, SRT or VTT.");
    }
    private static string Wrap(string text, int width)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries); var output = new StringBuilder(); var line = 0;
        foreach (var word in words) { if (line != 0) { if (line + word.Length + 1 > width) { output.Append('\n'); line = 0; } else { output.Append(' '); line++; } } output.Append(word); line += word.Length; }
        return output.ToString();
    }
    public static void Save(string path, string text, bool bom = false)
    {
        var part = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(part, text, new UTF8Encoding(bom)); File.Move(part, path, true); }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
}
