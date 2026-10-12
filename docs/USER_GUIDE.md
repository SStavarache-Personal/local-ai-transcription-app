# Local Transcriber user guide

See the [Library and Speech-to-text screenshots](../README.md#screenshots) for an overview of the app and floating widget.

## Start

Extract the whole portable ZIP. Open `LocalTranscriber.exe`. Windows x64 and AVX2 are required; 16 GB RAM is recommended, 32 GB gives more headroom. No model weights load at startup.

1. **Add workspace** selects a folder. Multiple pointers persist between launches. Rename/Move up organizes them. Remove pointer leaves all source files and results intact. For unavailable folders, use Locate folder or Refresh.
2. **Add files** links recordings; **Import copy** explicitly copies them into the workspace. Drag/drop also links. FFprobe validates actual media streams. Select a recording to see duration, tracks, channels, codecs and sample rate.
3. **Models** lists actual installed state, size, language/capability notes, estimated RAM, licence and provenance. Select **Download selected model** deliberately. Download bytes are real; Cancel retains partial data for retry. Complete size and SHA256 verification precedes installation.
4. **Transcribe** selects an installed speech model, audio track, language and cleanup. Parakeet selects language automatically. Whisper supports forcing the listed languages and optional terminology hints. Advanced VAD threshold controls speech detection. Enhanced audio can alter voices; compare the original/enhanced 15-second previews. A transcription creates a new immutable result.
5. In **Library**, Open result opens the timed editor. The queue runs one CPU model at a time. Cancel stops workers; Resume selected reuses matching checkpoints for canceled, failed or interrupted jobs. If the source changed, import it again and create a new job.
6. **Transcript** supports playback, selecting a segment to seek, double-clicking to seek again, search, text edits, manual speaker labels, Undo/Redo and Save edits. The raw model result remains accessible in the version selector. Windows may not play every FFmpeg-supported codec; **Compatible audio preview** makes a local WAV with the original timeline. Short A/B previews cover only the first 15 seconds.
7. Export TXT, Markdown, JSON, CSV, SRT or VTT. Save As prompts before overwriting. TXT/Markdown/CSV offer timestamp options; SRT/VTT always require timing, while canonical JSON always retains it. CSV BOM helps Excel. Speaker labels are included only when manually assigned. Export from the explicitly selected transcript version.
8. **Summarize** requires an installed GGUF language model. Choose from the LFM2.5, Qwen and Gemma downloads. Enter context/terminology, save/import/export a glossary, select a template and output language. Context terms do not prove what was said. Summaries can contain errors; review numbers, names, decisions and cited source text. Long input uses multiple saved passes and displays that fact. Edit/save/export the summary independently of the generated original.

## Storage and privacy

App preferences/index/models/cache live under `%LOCALAPPDATA%/LocalTranscriber`. Job directories live under `workspace/.localtranscriber/jobs`; read-only workspace output falls back to app data with a stage note. All source recordings remain untouched. Generated summaries and human revisions retain history.

Settings can change model/cache folders. Model moves verify copied data and keep the previous copy for manual removal after inspection. Remove model asks for confirmation and touches only model storage. Clean cache removes only app-owned preview/inspection files, after confirmation. It does not remove models, transcripts or originals.

Inference is local CPU. No cloud API, telemetry or automatic update checks are used. Microphone recording starts only when you press Record. LLM inference starts a temporary authenticated local-only process. Internet is needed only for explicit model downloads/provenance links. Once models are installed, inference can run without internet.

## Dictation and the speech workbench

On first use, **Help** opens the getting-started tutorial. Complete it with **Got it**; use **Replay getting-started tutorial** to reopen it. Help sits between Models and Settings in the wider sidebar.

In **Speech-to-text**, choose an installed speech model, microphone and spoken language. Parakeet uses automatic language detection; Whisper supports forcing the model's supported languages. The microphone list includes the Windows default input. Use **Refresh microphones** after connecting a device.

Press **Record test**, speak and watch the expanding volume circle. **Stop & transcribe** produces text in the workbench. **Cancel** stops capture or processing and keeps recorded audio. **Test audio file** uses the same pipeline, so you can compare different models on the same recording. Review and copy the final result; the raw and vocabulary versions remain visible separately. The last completed result is restored on restart.

Raw ASR and processed output appear side by side. Timed ASR segments often produce a newline per segment; these breaks are flattened into spaces before vocabulary replacement, including with LLM processing switched off. The raw column keeps the exact original output. Natural paragraphs added by the language model remain in the final result. File transcripts retain their timed segments.

**Speech-to-text** and **LLM** timings show elapsed wall time for each stage, including preparation, model verification/loading and generation, excluding microphone recording time. LLM shows **off** when disabled. Previous results without measured timings show a dash. **Apply selected prompt to this speech** reuses the same raw ASR, with the original ASR time and a fresh LLM time; it does not rerun ASR or insert into another app. Earlier processed results are retained in the job's `dictation-versions` folder.

## Speech-processing prompts (1.2)

Select **Light cleanup**, **Document structure**, or a saved custom prompt. Expand **Edit system instructions and output format** to rename or fully replace its system instructions. **New prompt** copies the selected prompt into an independent choice; save it with **Save prompt**. You can delete custom choices. **Restore default instructions** restores the selected built-in preset, or light-cleanup instructions for a custom choice, while retaining its name. The widget displays and uses the selected workbench prompt when **Process with the language model** is on.

The system text is a complete override of the built-in editing instructions. The selected output-format rule is appended separately. **Allow Markdown output** permits Markdown and keeps it in the copied/inserted text. With the option off, the model is instructed to produce plain text; a local Markdown parser also removes leftover formatting, preserving paragraph spacing, link addresses, code content and table cell separators. No rendering or network service is involved. Custom instructions can intentionally change how much content is retained; review the raw and final columns before using important results.

**Models** offers Qwen3.5 0.8B, 2B and 4B plus Gemma 4 E2B and E4B in Q4_K_M. These downloads provide text processing and summaries in this app. The multimodal families' image/audio functions are not exposed by these text-model entries. Models are downloaded separately and are not included in the portable ZIP.

For dictation into another application:

1. Save speech settings and open the floating widget.
2. Click the destination application's editable text box and place its cursor or select the text you want replaced.
3. Click **Record** in the widget. Its controls keep the other application focused.
4. Speak, click **Stop & transcribe**, and keep the original field focused until processing finishes. The result is inserted at its current selection. The widget does not submit or send the text.

If focus changes, the field is a password/read-only control, Windows blocks input (including elevated apps), or cleanup fails, automatic insertion is stopped. Review and **Copy result** from the workbench. Insertion uses paste to preserve characters and paragraphs; the previous clipboard is restored after the field confirms the inserted text, unless the clipboard changed meanwhile. If paste cannot be confirmed, the dictation remains on the clipboard and **Restore previous clipboard** appears in the workbench. Review the destination before restoring it. Copy deliberately changes the clipboard. Some third-party controls may not support these input/accessibility patterns; Copy is the fallback.

**Process with the language model** is optional and defaults off. Choose an installed language model and a prompt. **Light cleanup** removes fillers, accidental repetition and retracted wording, improves punctuation, and adds paragraphs at topic changes while retaining the full meaning. **Document structure** organizes the speech into a document. Processing runs locally on CPU and takes longer than transcription alone. AI can misunderstand corrections or alter meaning: compare the raw and processed columns for important names, numbers and instructions. Output/context-limit failures keep the vocabulary-corrected text for review. With the default light-cleanup instructions, a configured vocabulary term that disappears also triggers this fallback; full custom overrides control their own treatment of content.

## Vocabulary replacements

Edit **Vocabulary map** in Speech-to-text and save. The example rules map `M-A request` to `M-A-A request` and `MA request tracker` to `MAA request tracker`; edit/remove them or add other exact spoken variants, such as `M A request tracker`.

Rules ignore letter case, match whole words/phrases, and use literal text (no regular expressions). Longer matching phrases take priority. Replacement happens in one pass: a replacement is not fed into another rule. Disable **Apply vocabulary replacements** to bypass the map. Rules apply after ASR to newly created file jobs and microphone/workbench runs, before any optional LLM cleanup. Existing results are not rewritten. Rules are snapshotted in each job; `vocabulary.json` records what was applied. Raw ASR is immutable, while mapped file transcripts are saved as the initial edited version and used by normal exports.

Recordings live in `%LOCALAPPDATA%\LocalTranscriber\dictation\recordings`. Completed dictation jobs appear under the **dictation** workspace in Library. Each job keeps the raw transcript, mapped transcript when different, and `dictation.json` with raw/mapped/final text and cleanup status. Use **Open saved recording and results** to locate them. Recordings remain on disk until you remove them; Cancel does not erase them. Closing the app stops microphone capture.

## Recovery

- **No model**: download or import a compatible model in Models.
- **Low RAM/disk**: choose a smaller model, close other apps, reduce summary context/output, or move storage.
- **Failed download**: retry; a range-capable server resumes staging files. Bad checksums never install.
- **Unsupported playback**: use compatible audio preview. Transcription still uses FFmpeg decoders.
- **Interrupted job**: Resume selected. Changed source metadata or model revision requires a new run.
- **Bad imported model**: the app rejects incompatible metadata or failed CPU inference. Use a verified catalogue variant.
- **Missing runtime**: re-extract the entire portable ZIP; do not move the EXE alone.
- **Summary context too large**: shorten terminology/system instructions or use a larger supported window. Source text is chunked rather than silently truncated.

Keyboard: Ctrl+O adds recordings; Ctrl+S saves transcript edits; Ctrl+F focuses transcript search. Standard Windows controls support Tab, keyboard editing and screen-reader names. Theme follows Windows or can be set explicitly to Light/Dark.

Automatic diarization, word alignment, AI correction proposals, Qwen3-ASR and Moonshine are unavailable in this release. Speaker labels are human entries; no identity is inferred.
