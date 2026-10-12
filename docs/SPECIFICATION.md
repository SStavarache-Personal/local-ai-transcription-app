# Local Transcriber — Windows Desktop Application Specification

**Document type:** Implementation-ready product and engineering specification  
**Version:** 1.0  
**Date:** 2026-10-08  
**Status:** Ready for autonomous implementation  
**Primary target:** Windows 11, Windows 10 where feasible, x64 CPU-only, 16 GB / 32 GB system RAM  
**Distribution:** Self-contained desktop executable; downloaded models and user data remain external to the binary

---

## 0. Instructions to the implementation agent

Build the complete working application, not a mockup, proof of concept, or disconnected user interface. Take ownership of design, implementation, packaging, testing, documentation, and production of a working portable Windows build.

- **No clarification is needed to start.** Use the defaults and priorities below. Resolve implementation details independently, documenting consequential decisions in `docs/DECISIONS.md`.
- **Inspect the local development environment first.** Determine installed .NET SDK, Windows App SDK/WinUI 3 prerequisites, Visual Studio build tools, MSVC, CMake, Python, FFmpeg, Git, and any relevant tooling. Reuse installed tooling when practical; install *development-only* dependencies as needed with appropriate permissions. **Do not require end users to install these tools.**
- Prefer **native-feeling, fast Windows UI and CPU performance** over framework loyalty. WinUI 3 + Fluent Design is preferred when buildable. A fallback is allowed under Section 3.
- Build incrementally but deliver a real end-to-end workflow: select workspace → import media → preprocess → run locally downloaded ASR model → review/edit transcript → export → summarize with local LLM → export summary.
- Use real models, inference engines, media probing, audio preprocessing, and downloads. Do not simulate results, durations, progress, transcription, or model compatibility.
- Treat specifications marked **MUST** as mandatory; **SHOULD** as important; **MAY** as optional. Priorities P0/P1/P2 indicate delivery order, not permission to omit P0.
- In case of upstream model changes, verify the current repository, runtime support, license, model artifacts, and required companion files. Pin tested versions and explain deviations. Never substitute an unsupported model silently.
- Do not invent model features or hardware performance. Where an engine cannot perform a feature, expose it as unavailable with an explanation.
- Avoid cloud APIs and paid services. External internet access is **only** for user-initiated downloads/updates, not for inference.
- **Completion criteria:** provide runnable app artifact(s), source code, build instructions, dependency/license manifest, test evidence, and a clear account of any remaining gaps.

## 1. Product definition and outcomes

**Purpose:** A fully local, standalone Windows desktop application for transcribing audio and video files using interchangeable open-weight speech recognition models, then optionally correcting/structuring and summarizing transcripts using local language models.

**Users:** Non-technical Windows users handling meetings, interviews, lectures, recordings, and multilingual material. No command-line knowledge expected.

**Core principles**

1. **Local by default.** User audio, transcripts, summaries, glossary, and prompts never leave the device; no telemetry or network listening during processing. Model downloads require deliberate user action.
2. **CPU first.** All core functionality must run without discrete or integrated GPU acceleration and without CUDA. Optional hardware acceleration in future is allowed but not necessary for acceptance.
3. **Fast and stable.** No freezing the UI, unbounded RAM use, unnecessary full-file decoding, or compulsory parallel model loading.
4. **Modular providers.** Each ASR and LLM runtime is independently replaceable. Models are assets—not code modifications.
5. **Transparent capabilities.** Non-technical descriptions, clear hardware recommendations, disk sizes, model capabilities, task stages, and honest limitations.
6. **Non-destructive.** Original media, raw ASR output, edits, and AI-assisted revisions remain separately retrievable.
7. **Functional interface.** Windows Fluent-inspired UI with restrained typography, intuitive navigation, accurate state, and no marketing copy or filler slogans.

**Out of scope for initial release:** Cloud inference; live microphone dictation; simultaneous live meeting capture; video frame understanding; full DAW audio editing; transcription of DRM-protected streams; cross-device sync; translation unless explicitly implemented and verified as a separate operation.

## 2. Delivery priorities

### P0 — Required first working release

- Standalone offline-capable Windows executable distribution with bundled runtime dependencies.
- Multiple persistent workspace-folder pointers, import media, supported formats through FFmpeg, in-app media preview.
- Model catalogue, on-demand model download, import and removal; **Parakeet TDT V3 + whisper.cpp** working in CPU mode, with at least Whisper Small/Medium and Large V3 Turbo choices.
- Language choice (Auto, English, French, other verified language choices), accurate capability constraints.
- Real pipeline: probe → extract/decode → safe audio preprocessing → segment → recognize → merge → review.
- Persistent job queue, stage and overall progress, job cancellation, failure recovery, original preservation.
- Editable time-aligned transcript viewer, basic playback navigation, search, exports (TXT, Markdown, JSON, CSV, SRT, VTT).
- Local GGUF LLM summarization using **llama.cpp**, with **LFM2.5-1.2B-Instruct and LFM2.5-2.6B** available; context/glossary, presets, summary viewer, export.
- System resource assessment, RAM-aware context suggestions, model disk usage, model deletion.
- Functional tests, packaged executable, user guide, build instructions.

### P1 — Important completion features

- Qwen3-ASR 0.6B and 1.7B **where a verified Windows CPU runtime handles their audio inputs correctly**; Moonshine V2 English model(s) where supported.
- Additional LFM2.5 models 230M, 350M, 1.2B-Thinking and 8B-A1B; runtime-specific optional quantizations.
- Selectable denoising/voice enhancement, silence trim/normalization, enhanced VAD settings and A/B comparisons.
- Multi-pass model comparison, discrepancy marking, second-pass correction suggestions.
- Optional speaker labeling/diarization through a separately verified provider, with human correction.
- Word-level alignment through a compatible aligner when available; editable speaker labels; glossary import/export.
- Batch operations, resumable stage checkpoints, summary templates and context-preserving chunked summarization.

### P2 — Optional extensions

- Hardware acceleration and ARM64 if independently verified.
- More ASR engines, more local GGUF LLM families, advanced denoising options, performance benchmark panel, project search index.
- Auto-generated chapter markers, speaker statistics, audio waveform rendering, keyboard-driven segment editor, LRC export or model-specific advanced exports.

## 3. Platform, UI framework, and distribution

### 3.1 Runtime requirements

- Windows 11 x64 primary; Windows 10 x64 as feasible under chosen GUI/runtime.
- 16 GB RAM minimum recommended; 32 GB preferred. Lower RAM must receive an explicit warning and conservative configuration, not an immediate crash.
- CPU-only execution required. Detect x64 instruction sets (including AVX2) before selecting native inference binaries, and provide a compatible build or clear support limitation on older hardware.
- Works as a standard user; no administrator rights after installation/extraction.
- Works offline after downloading required models. No Python, Node, developer tools, .NET SDK, Visual Studio, Ollama, or preinstalled FFmpeg required on an end-user machine.

### 3.2 Preferred stack

| Layer | Preferred choice | Reason / fallbacks |
|---|---|---|
| Desktop UI | **C# / .NET, WinUI 3 / Windows App SDK, Fluent Design** | Windows-native controls, accessibility, system themes. If build environment cannot reliably produce it, **WPF + Fluent styling** is preferred fallback; **Tauri + React/Fluent UI** is acceptable only with demonstrated performance and dependable packaging. Do **not** choose Electron by default. |
| Application services | .NET services and process manager | Async jobs, SQLite, model manager, process supervision. |
| Data persistence | SQLite + JSON manifests | Local, transactional, portable. |
| Media decoding | Bundled FFmpeg + ffprobe | Broad container and codec support. |
| Parakeet | ONNX Runtime CPU via onnx-asr or equivalent tested wrapper | Quantized INT8 whenever supported. A bundled isolated Python worker is allowed **only if fully packaged**; native .NET ONNX adapter preferred where practical. |
| Whisper | **whisper.cpp** packaged CPU executable/library | Native GGML/GGUF-compatible variants as actually supported by the installed whisper.cpp version. |
| Qwen3-ASR | Verified llama.cpp audio path or model-specific CPU inference worker | Do not treat generic text-only GGUF loading as proof of speech recognition. |
| Moonshine | Official Moonshine V2 Windows/C++ runtime | Separate adapter; English first. |
| Summarization | **llama.cpp** packaged CPU runtime + GGUF | Supports LiquidAI LFM2.5 catalogue and custom GGUF models. |
| Denoising | CPU-capable RNNoise or DeepFilterNet (optional) | Must be benchmarked against original audio and never applied destructively. |
| VAD | Silero VAD ONNX or equivalent | Dedicated silence/speech segmentation. |

**Distribution preference:** portable self-contained app distributed as an EXE where reliable; a ZIP of a self-contained executable and bundled native libraries is acceptable if model runtimes make a truly single-file package fragile. For WinUI 3, investigate unpackaged/self-contained `PublishSingleFile` (Windows App SDK 1.5+) **and test it on a clean machine**; its dependencies may extract at first launch. The user-facing application must launch from an executable. Downloaded models and workspace contents are **not** embedded in that EXE.

**Required delivery artifacts**

1. `dist/LocalTranscriber.exe` **or** `dist/LocalTranscriber-portable.zip` with obvious `LocalTranscriber.exe` inside.
2. Source tree, reproducible build script (`build.ps1`), test runner (`test.ps1`), pinned dependency versions.
3. `README.md` (install/run), `docs/USER_GUIDE.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/TEST_RESULTS.md`, `THIRD_PARTY_NOTICES.md`.
4. A small permitted sample fixture and a test that exercises the whole pipeline; do not bundle copyrighted audio or large models without explicit right to redistribute.

## 4. Information architecture and user experience

### 4.1 Main navigation

Use a left NavigationView/rail or equivalent:

- **Library** — workspaces, files, jobs and projects.
- **Transcribe** — selected file, model, audio settings, progress.
- **Transcript** — timed editor, audio playback, speaker labels, exports.
- **Summarize** — model, user context, template, generation, preview, export.
- **Models** — ASR models, LLM models, downloads/imports/deletion.
- **Settings** — device, storage, performance, defaults, advanced settings.

Where practical, use a file detail view with `Transcript` and `Summary` tabs to avoid unnecessary navigation, while retaining a clearly discoverable workflow.

### 4.2 UI details

- Use Segoe UI / modern Windows typography, Fluent-style controls, 8-point-based spacing, restrained icons, ample padding, light/dark/system themes, good contrast, Windows 11 visual conventions, keyboard shortcuts, tooltips, screen-reader labels, and high-DPI support.
- No exaggerated taglines, exclamation-filled onboarding text, decorative charts, useless status labels, or forced tutorials.
- Name controls by purpose: `Add workspace`, `Add files`, `Speech model`, `Language`, `Remove background noise`, `Transcribe`, `Export transcript`, `Summarize`, `Download model`.
- Technical descriptions should be short and practical: e.g. `Whisper Medium — Good balance of accuracy and CPU use; supports many languages; slower than Parakeet on some computers.`
- Empty states explain the next useful action. Errors state what failed and how to recover, without blaming the user.
- Never present a disabled feature as enabled or a model as installed when it is not.
- Clear progress shows actual stages and status, never artificial percentages/time remaining. Allow indeterminate status when metrics are unavailable.
- For each model, display plain-language `Best for`, supported languages, CPU suitability, optional features, approximate on-disk size, installed/not installed, storage location, license and provenance link.

### 4.3 Suggested layout

**Library:** workspace selector at upper left; `Add workspace`, `Remove pointer`, `Open in Explorer`; file list with type, duration, created/imported date, transcript status; detail preview in right panel. Search/filter and recent items.

**Transcribe:** file picker / drag-drop, media preview; model selector with compact capability card; `Language` ComboBox; `Audio cleanup` selector (`Automatic`, `Off`, `Enhanced`); advanced expandable section; `Transcribe` button; chronological stage list and progress with cancel. No requirement to visit an advanced settings page for normal use.

**Transcript:** synchronized audio/video player; searchable transcript with clickable timestamps, speaker tags when available; edit and revert; export button with format and options.

**Summarize:** summary model selector with hardware recommendation, editable `Context / terminology` text area, optional glossary, output template selector, language, generation progress; summary and source transcript panels; export action. `Advanced` section for temperature, context window, thread count, top-p/top-k, output limit, system prompt override.

**Models:** two tabs, `Speech recognition` and `Summarization`; cards/table with search, language filters, downloaded size, actual installed state, download progress, import, remove and inspect location.

## 5. Workspaces, files, and storage

### 5.1 Workspace pointers — P0

- Users can **add, switch, rename display labels, reorder, and remove workspace pointers** to any accessible filesystem folders.
- Workspace pointers persist between launches; they are *references* to folders, not a hidden duplicate of all media.
- `Remove workspace` removes only the app's shortcut/database reference. It **never** deletes user files or exported data. Explicit explanatory dialog.
- When a workspace is offline, renamed, moved, or inaccessible, show `Folder unavailable` and offer `Locate folder`, `Retry`, or `Remove pointer`.
- Per-workspace files: add multiple media files via Windows file dialog, drag-and-drop, or optional `Import to workspace` file copy (explicit copy vs link). Multiple workspaces can be used independently.
- Do not automatically scan every subfolder recursively on launch. Lazy indexing and incremental refresh.
- Preserve Unicode names, long Windows paths where supported, OneDrive paths, read-only source files, removable drives, and names with whitespace.

### 5.2 App-owned output organization

Default non-destructive folder structure within each writable workspace (configurable; if read-only, use an app-data fallback with an explicit note):

```text
<workspace>/
  <original-media-files>            # never modified
  .localtranscriber/
    workspace.json
    jobs/
      <job-id>/
        job.json
        transcript.raw.json          # immutable ASR result
        transcript.edited.json       # user edits, versioned
        transcript.corrected.json    # optional AI suggestions/approved corrections
        summary.json
        temporary/                  # disposable, cleaned safely
    exports/
      <basename>/
        transcript.md
        transcript.txt
        transcript.csv
        transcript.json
        transcript.srt
        transcript.vtt
        summary.md
```

Do not pollute source folders with opaque caches unless a workspace-level option is enabled. SQLite app index and global preferences live under `%LOCALAPPDATA%/LocalTranscriber/`, with `models/`, `logs/` and `cache/` separate from user workspaces. Support user-configurable model/cache locations and move with verification.

### 5.3 File compatibility and preview

- Use **FFmpeg/ffprobe**, not file extensions, to validate actual supported streams.
- Target audio: `.wav`, `.mp3`, `.m4a`, `.aac`, `.flac`, `.ogg`, `.opus`, `.wma`, `.aiff`, `.aif`, `.webm`, `.amr` where bundled decoder supports it.
- Target video: `.mp4`, `.mov`, `.mkv`, `.webm`, `.avi`, `.wmv`, `.m4v`, `.mpeg`, `.mpg`, `.ts` where decodable.
- Support additional FFmpeg-compatible containers/codecs when safe; don't promise universal format support or DRM bypass.
- Audio track selection for media with multiple tracks; default to first appropriate track, allow changing selection.
- In-app playback should use available Windows media components or a tested bundled decoder. Handle unsupported preview format gracefully while permitting transcription through FFmpeg when possible.
- Show codec, duration, channels, audio tracks, sample rate, source file size. Never load an entire multi-hour file into UI memory.

## 6. Processing pipeline

### 6.1 Real stage model

```text
Import / verify source
  → ffprobe media information
  → extract selected audio track and decode to suitable PCM
  → optional gain normalization and denoising
  → voice activity detection / chunking
  → ASR per chunk (or native long-form engine path)
  → timestamp alignment and deterministic segment merging
  → save immutable raw transcript
  → user review/edit and optional speaker labels
  → export and/or local summary
```

- Automatically respect the sample rate/channel/layout actually required by the chosen engine; common choice is 16 kHz mono PCM, **but it must not be hard-coded for every engine**.
- For stereo recordings, retain original channel information as metadata; optional channel-aware diarization where reliable.
- No destructive replacement of original source. Cache enhanced temporary audio only until no longer needed or user explicitly chooses to retain it.
- Normalize audio only when useful; avoid clipping and amplification of background noise; optional peak/loudness normalization.
- Default `Audio cleanup = Automatic`: check approximate signal characteristics and use modest, safe processing. Do not automatically invoke heavy enhancement on every recording without a measurable benefit.
- `Off`: original decoded audio, no denoising. `Enhanced`: stronger noise suppression (RNNoise/DeepFilterNet where installed) with warning of potential voice artifacts.
- Provide a 10–20 second optional A/B preview of original and enhanced audio. When an enhancement crashes or fails, offer fallback to original and preserve job state.
- Use VAD for Parakeet because common ONNX recognition implementations are limited to ~20–30 second segments; chunk with configurable padding/overlap to preserve words at boundaries.
- Avoid dropped/duplicated words at chunk boundaries; reconcile by time ranges and conservative text merging, not simple string concatenation.
- Multi-hour input must be stream-processed, with bounded memory, per-chunk checkpoints and time offsets relative to original video/audio timeline.
- Job metadata must record source hash or path/size/mtime, selected audio track, exact model ID/version/checksum, preprocessing parameters, language mode, run time, and warnings.

### 6.2 Job system and status

- Persist job queue in SQLite and per-job manifests; states: `Queued`, `Preparing`, `Extracting`, `Enhancing`, `Segmenting`, `Transcribing`, `Aligning`, `Saving`, `Completed`, `Canceled`, `Failed`, `Interrupted`.
- Display completed stages, current stage, per-stage progress, elapsed time, processed audio duration / total duration, and estimated remaining time **only when trustworthy**.
- Cancel stops the underlying child process or worker cooperatively and prevents writes of incomplete final files. Kill if necessary after timeout, and clean orphan processes at next launch.
- Pause/resume where supportable at chunk boundaries. On crash/restart, mark interrupted jobs and offer `Resume` from validated intermediate results rather than silently restarting.
- Allow multiple queued jobs, but default to **one active CPU-heavy inference job** on 16 GB devices. ASR and LLM must unload before loading a different heavy model by default.
- Progress remains responsive even with 100% CPU work; no UI-thread blocking.
- Log underlying technical error details to local logs, while presenting an actionable non-technical summary.

## 7. Speech recognition engine and model architecture

### 7.1 Provider contract — mandatory

Every ASR engine adapter exposes capabilities and a unified interface. Suggested shape (implementation language may differ):

```text
AsrProvider:
  id, displayName, runtimeName, runtimeVersion
  listSupportedModels()
  inspectModel(path) -> validation result
  capabilities(model) -> {
    languages, languageAutoDetect, forcedLanguage,
    segmentTimestamps, wordTimestamps,
    speakerAttribution, translation,
    textPromptSupport, streaming, minMemoryEstimate,
    quantization, maxAudioChunkSeconds
  }
  prepareJob(input, settings) -> plan
  load(modelPath, cpuThreads)
  transcribeChunk(audioChunk, originalOffset, language?, hints?, cancelToken)
  unload()

CanonicalTranscript:
  id, sourceFile, sourceTrack, durationMs, language, model, createdAt,
  segments: [
    {id, startMs, endMs, text, speakerId?, words?[], confidence?, flags?[]}
  ],
  fullText, metadata, processingWarnings
```

- Models should be identified by unique registry IDs, not just filenames. Distinguish model, quantization and runtime variants.
- Preserve nullable/unknown capabilities. No invented confidence scores, speaker IDs, or word-level timestamp precision.
- Store engine's full native output as an **optional diagnostic sidecar** only when explicitly enabled; always normalize the data needed for standard exports.
- Distinguish `supported by base model` vs `supported through additional aligner / diarizer / app feature`.

### 7.2 Built-in ASR catalogue

| Model / profile | Required priority | Runtime / download notes | User-facing guidance |
|---|---|---|---|
| **NVIDIA Parakeet TDT 0.6B V3 (multilingual)** | **P0 / default** | Official model `nvidia/parakeet-tdt-0.6b-v3`; verified ONNX CPU conversion through `onnx-asr`; prefer INT8 if accurate and available. Multilingual, includes English/French; its default behavior includes automatic language identification. | **Recommended for most recordings.** Fast CPU transcription; supports English and French and other European languages. |
| **Whisper Small (multilingual)** | **P0** | Official Whisper weights adapted for `whisper.cpp` CPU runtime, supported quantizations. | Lower memory use; good for shorter, clearer material. |
| **Whisper Medium (multilingual)** | **P0** | `whisper.cpp` CPU; quantized variant where verified. | Balanced multilingual alternative; generally more compute than Small. |
| **Whisper Large V3 Turbo** | **P0** | `whisper.cpp` CPU with verified released variant and quantization. | Strong multilingual compatibility and mature tooling; may run slowly on CPU. |
| **Whisper Large V3 (full)** | **P1** | `whisper.cpp` compatible CPU model, warning about memory and speed. | Higher compute; useful as alternative quality reference; not always better on every file. |
| **Qwen3-ASR 0.6B** | **P1** | `ggml-org/Qwen3-ASR-0.6B-GGUF` or upstream `Qwen/Qwen3-ASR-0.6B`, tested with **audio-capable** Windows CPU runtime and required projector/processor assets. | Multilingual alternative; useful when fast primary output is uncertain. |
| **Qwen3-ASR 1.7B** | **P1** | Equivalent supported GGUF or CPU-capable runtime; test real audio decoding. | Higher-compute multilingual accuracy candidate. |
| **Moonshine V2 English (small/medium availability-dependent)** | **P1** | Official `moonshine-ai/moonshine-v2` Windows/C++ implementation and available English weights. | Efficient English on-device option; **do not list French support unless a compatible French checkpoint is independently verified**. |

**Important:** Qwen ASR GGUF support requires confirming end-to-end audio preprocessing/projector and output; a successful model load or text generation test does not validate speech recognition. Likewise, Moonshine's availability, language set and speaker features must be verified per version before enabling in production.

**Language ComboBox:** `Auto detect`, `English`, `French`, then alphabetically list languages supported by selected model. If model internally auto-detects and cannot force language, show: `This model selects the spoken language automatically. Your selection is a preference, not an enforced language.` Better: disable forced language for that model and show a concise explanation; preserve user intent for a model that supports it. `Auto detect` applies per file or per segment only where actually supported. Code-switching may reduce accuracy; tell users when relevant.

**Model guidance:** Choose recommendations based on CPU/memory/installed size and capability evidence, not a hard-coded universal accuracy ranking. Display known constraints for accented French, overlapping speech, music, high noise and technical vocabulary.

### 7.3 Feature support and optional extensions

- **Timestamps:** minimum P0 segment start/end; word-level timestamps are P1 through native functionality or separate forced aligner. Indicate which produced them.
- **Speaker identification:** not a default ability of Whisper or Parakeet. Implement via separate compatible speaker diarization pipeline, validated stereo-channel mode, or verified Moonshine speaker support, with speaker labels (`Speaker 1`, `Speaker 2`) and manual reassignment. Never claim to know real names unless the user assigns them.
- **Whisper-specific features:** expose native translate-to-English / transcription prompts only where compatible with chosen checkpoint/runtime; ordinary user does not need to see implementation flags.
- **Speech confidence:** show only if output contains a meaningful supported measure; no fake numerical `accuracy %`.
- **Model comparison:** P1 `Transcribe again with another model` creates another immutable result. Diff transcript segments by timestamps and show mismatches for user verification, not as an automatic truth verdict.
- **Vocabulary hints:** `Key terms / names` per workspace/project. Feed to ASR only when that engine genuinely supports prompt/hotword biasing; otherwise use for optional review and LLM-assisted correction, with explicit provenance.

## 8. Model manager

### 8.1 Catalogue and installations

- Ship a small, versioned model-registry manifest (not multi-GB model weights), and permit catalogue updates only on request.
- For each entry display: model family, developer, model size/parameter count when documented, quantization, `Download size` (from actual artifact manifest), `Required disk space`, **estimated** run-time RAM range, supported languages, hardware rating, capabilities, license and model card link.
- Fetch from verified model source/release (Hugging Face or official GitHub). Pin revision, artifact list, hashes where published, expected directory structure, license, and runtime compatibility in registry. HTTPS only. Confirm redirects/expected origin.
- Downloads must show actual bytes/total bytes, progress, cancel, retry/resume where server supports range requests, and clear error for disk/network failure.
- Download to staging, verify size/hashes, atomic move into model store. No incomplete model marked installed.
- Offer import from a compatible **GGUF file** (LLM or verified ASR GGUF), `whisper.cpp` model file, or compatible **ONNX model directory** with tokenizer/config/other required assets. Inspect metadata before registering; do not claim any arbitrary file is usable.
- Model import validation should verify type, architecture, required companion files, supported runtime and actual load (via safe isolated validation worker). Explain incompatibility clearly.
- Model delete shows disk space reclaimed and whether in use; queue deletion until unloaded, confirmation required. Removing models must **not** delete transcripts, summaries or projects.
- Do not run custom code supplied by model repositories. Support only recognized weights/data formats in pinned runtimes; no `trust_remote_code` from random imported repos.
- Respect upstream licenses for weights, code, binary distribution and commercial use, including any **Liquid AI LFM License 1.0** terms (do not mislabel it as Apache/MIT); publish notices and attribution.

### 8.2 Disk layout and controls

```text
%LOCALAPPDATA%/LocalTranscriber/
  settings.json
  library.sqlite
  models/
    asr/<registry-id>/<revision>/<variant>/...
    llm/<registry-id>/<revision>/<quantization>.gguf
  runtime/              # when generated or extracted
  cache/
  logs/
```

- Show total size per model family and overall model storage. Users can change model download directory and move models with progress, verification and rollback.
- Cache cleanup setting must distinguish disposable temp data from model weights and saved user outputs.
- If running as a portable ZIP/exe, use app-local storage **only if writable and explicitly selected**; default to LocalAppData, not the executable's potentially protected directory.

## 9. Local LLM summarization and optional transcript correction

### 9.1 Supported GGUF family (Liquid AI LFM2.5)

Use **llama.cpp CPU** as the first summarization provider. Support official LiquidAI GGUF repos and verified quantizations:

| Text model | Priority | Typical use | 16 GB / 32 GB posture |
|---|---|---|---|
| **LFM2.5-230M** | P1 | Very lightweight extraction / simple structured labels; not the default for comprehensive summaries | Lightweight both |
| **LFM2.5-350M** | P1 | Fast extraction, brief structured summaries and tests | Lightweight both |
| **LFM2.5-1.2B-Instruct** | **P0** | Faster, lower-memory summary and structured notes | Recommended for 16 GB |
| **LFM2.5-1.2B-Thinking** | P1 | Optional reasoning-oriented variant; potentially more latency/output overhead | Supported, not necessarily ideal for straightforward summaries |
| **LFM2.5-2.6B** | **P0 / quality recommendation** | Detailed summary and extraction; supports larger advertised context, subject to RAM | Recommended when memory headroom is available |
| **LFM2.5-8B-A1B** | P1 | Larger MoE quality candidate; quantized model weights around ~5 GB at Q4_K_M, but **runtime memory also includes KV cache/overheads** | Conditional for 16 GB, more comfortable at 32 GB; benchmark CPU throughput |

Start with `Q4_K_M` or model-specific QAD Q4_0 where tested. Offer Q5/Q6/Q8 alternatives with size/memory/performance explanations. The nominal GGUF file size is **not** total runtime RAM, nor is parameter count equivalent to inference speed. Models may include internal reasoning tokens; do not leak reasoning tags into final summary.

Source identifiers:

- `LiquidAI/LFM2.5-230M-GGUF`
- `LiquidAI/LFM2.5-350M-GGUF`
- `LiquidAI/LFM2.5-1.2B-Instruct-GGUF`
- `LiquidAI/LFM2.5-1.2B-Thinking-GGUF`
- `LiquidAI/LFM2.5-2.6B-GGUF`
- `LiquidAI/LFM2.5-8B-A1B-GGUF`

**Additional import:** allow compatible third-party GGUF text models under a general `llama.cpp GGUF` provider after inspection and successful smoke-test; not any arbitrary safetensors/PyTorch checkpoint.

### 9.2 Summary workflow — P0

1. Open existing transcript (raw or user-edited selection).
2. Choose summary model (auto-recommended based on available memory and performance).
3. Optional text field **`Context and terminology`** where user explains meeting subject, companies, product names, acronyms, expected technical terms, and conventions. Allow saved workspace-specific glossary/templates. This is **ordinary user context**, separate from system instructions.
4. Choose output language (`Same as transcript`, `English`, `French` where model supports the language).
5. Choose output style (`Concise summary`, `Detailed notes`, `Meeting notes`, `Decisions and actions`, `Key points`, `Custom template`). Do not generate empty headings or invent decisions/people.
6. Click `Generate summary`; show stage/progress and incremental output when supported.
7. Review, edit, save versions, export.

**Default stable system-instruction behavior** (editable only under `Advanced`):

> Summarize only information supported by the supplied transcript and user context. Preserve names, dates, numbers and technical terms carefully. Treat user-supplied terminology as candidate interpretations, not as evidence that those words were said. Do not invent facts, decisions, action owners or commitments. If a passage is unclear, say that it is unclear or omit unsupported detail. Follow the selected output structure and language.

Provide a separate **`Suggest transcript corrections`** action (P1) that uses context + glossary to propose likely corrected words/phrases, each linked to original segment/timestamps and marked as `AI suggestion`. Do **not** overwrite `transcript.raw.json`, claim audio verification, or silently replace uncertain names/numbers. Provide accept/reject controls and preserve before/after versions. Summarization may use raw, user-edited, or user-approved corrected transcript, chosen explicitly.

### 9.3 Long transcript and context handling

- Read actual model metadata for maximum supported context and selected runtime. Do not assume a long advertised context always fits in 16 GB RAM.
- Before inference, **tokenize the prompt** and calculate required tokens: system instructions + user context + template + source transcript + generation reserve. Do not use character counts as a substitute for token counting.
- Default to `Auto (recommended)` context sizing based on free RAM, projected model weight and KV/cache memory, CPU headroom, advertised max context, reserved output tokens, and OS safety margin.
- If the full transcript cannot fit, use a **bounded map-reduce summarization** path:
  - Split transcript at chronological/speaker boundaries with timestamps and limited overlap.
  - Extract grounded structured notes for each chunk with stable schema and source segment IDs.
  - Consolidate intermediate notes in hierarchical passes, preserving source references and unresolved ambiguities.
  - Deduplicate, merge topics and action items, and produce final output.
  - Save intermediate summary checkpoints, source anchors, and warnings.
- Warn when model cannot reliably summarize a long transcript at once. Never silently truncate beginning/middle/end of transcript.
- Cap context and batch sizes to prevent paging storms. Treat excessive `n_ctx` as risky even when the number is within the model's published maximum.
- Provide `Advanced` settings for max context tokens, threads, batch size, temperature, top-p, top-k, repetition penalty, max output tokens, custom system prompt, and seed if supported. Validate ranges; allow reset to defaults.
- Keep useful defaults conservative (e.g., low temperature around 0.1). Use model-specific sampling/chat template recommendations where documented; don't apply one template to all GGUF architectures.
- `Context and terminology` is never executed as commands or trusted as a higher-privilege prompt; source transcripts can contain adversarial instructions. Summarizer must treat both as untrusted data.

### 9.4 Summary provenance and format

- Save model ID/revision, quantization, settings, creation time, source transcript version/hash, output language, template, user context, and warning list.
- For actions/decisions, retain supporting segment references (timestamps) internally when the model can reliably produce them; validate cited IDs against source before displaying links. Do not show fabricated source anchors.
- Provide default exports: Markdown, TXT, JSON. Optional CSV for action lists and decisions. Maintain summary edits without destroying generated original.

## 10. Export and in-app review

### 10.1 Transcript exports — P0

Expose `Export transcript` dialog:

- **Format ComboBox:** Plain text (`.txt`), Markdown (`.md`), CSV (`.csv`), JSON (`.json`), SubRip (`.srt`), WebVTT (`.vtt`). Offer `.lrc` / richer formats only if implemented correctly.
- **Content:** `Full text`, `Segments`, `Word-level` (**only** when available).
- **Timestamps:** `None`, `Start of each segment`, `Start–end`, `Per-word` (if available).
- **Speakers:** `Include speaker labels` toggle, enabled only when there are speaker labels.
- **Transcript version:** `Raw model result`, `Edited`, `Approved corrected` (only when present).
- **Output destination:** chosen default `workspace/.localtranscriber/exports/<basename>/`; use Save As for overrides; clear overwrite or automatic version naming.
- **Encoding:** UTF-8, correct accents (French), BOM option in advanced CSV settings for Excel interoperability.
- **CSV:** stable schema: `segment_id,start_ms,end_ms,start_time,end_time,speaker,text,language,confidence` (null when unavailable); quote embedded commas/newlines correctly. Timestamp-free CSV should omit or leave time columns according to a clearly selected preset.
- **JSON:** versioned canonical schema with metadata, timestamps, speaker and optional words, even when export options filter views.
- **SRT/VTT:** valid subtitle timestamp standards, ordered non-overlapping time intervals, readable wrapped lines; speaker labels optional; enforce segmentation and time validation, not arbitrary copy of chunk offsets.
- **Markdown:** readable speaker/timestamp headings or bullet segments; choose clean narrative mode if timestamps disabled.
- **TXT:** plain unadorned text when timestamps disabled; optional segment-prefix mode.

**Important:** All model outputs normalize through the canonical schema. The application may offer formats/features beyond native ASR output, but must not imply that every ASR model produces timestamps, diarization or confidence fields natively.

### 10.2 Transcript/media viewer — P0

- Open media inside the app, play/pause/scrub; clicking a transcript timestamp seeks to that time in the original media.
- Virtualize long transcript lists; avoid rendering all words at once. Search, copy, select range, edit text, undo/redo and save.
- Distinguish original ASR output from user edits and AI suggestions. Visible unsaved change indicator.
- Speaker labels editable when available; manual speaker labeling possible without automated diarization.
- `Open export folder` and `Open source folder` via Explorer.
- Keep a project-level history of transcription runs, summary runs, selected settings, and versions.

## 11. Device inspection and adaptive resource management

### 11.1 Display to the user

Settings > Device / Resources:

- Windows version, CPU name, logical/physical cores, instruction sets, total and currently available RAM, detected GPUs (informational only), free disk space at model/cache/workspace paths.
- Clear tier classification such as `Suitable for lightweight models`, `Suitable for most models`, `Large models may be slow`; not bogus percent accuracy estimates.
- Per model: `Recommended`, `Supported but may be slow`, `May exceed available memory`, `Unsupported runtime`, `Not tested` based on conservative estimates and actual smoke tests.
- Let user optionally run a **local representative benchmark** for CPU threads/quantization and display measured audio seconds processed per wall second (ASR) or output tokens per second (LLM), with hardware and model details.

### 11.2 Safety and tuning

- Consider **available RAM**, not only installed RAM, when allocating context and loading multiple models.
- Reserve memory for Windows and other applications; avoid memory oversubscription and swapping. If load fails, report the model/config and recommend reducing context, selecting smaller model, or closing apps.
- Default CPU threads to a reasonable number of physical cores, and expose custom threads in Advanced.
- Use SIMD-optimized native CPU runtimes where supported. Pin CPU binary variants and include validated fallback if targeting non-AVX2 processors.
- Automatically unload ASR after transcript is saved before loading LLM. Do not keep both resident unless the user opts in and memory allows.
- Keep the GUI responsive while processing. Bound temporary PCM, waveform, token and transcript buffers.
- For 16 GB: prioritize Parakeet INT8 + Whisper quantized variants; LFM2.5-1.2B or 2.6B quantized with cautious context. For 32 GB: permit larger alternatives, but still benchmark and cap contexts.
- Do not auto-select maximum theoretical context length; choose **largest safe and useful** context subject to model and system constraints.

## 12. Application architecture, contracts and security

### 12.1 Logical components

```text
Desktop UI / ViewModels
   │
   ├── Workspace & Media Library Service ── SQLite / project manifests
   ├── Media Service ── ffprobe, FFmpeg, preview
   ├── Job Orchestrator ── durable queue, workers, checkpoints
   │      ├── Audio Enhancement + VAD providers
   │      ├── ASR Adapter: Parakeet / ONNX CPU
   │      ├── ASR Adapter: Whisper / whisper.cpp
   │      ├── ASR Adapter: Qwen3-ASR / verified CPU path
   │      └── ASR Adapter: Moonshine / verified CPU path
   ├── Transcript Repository + Editor + Exporters
   ├── Summarization Service ── llama.cpp CPU worker
   ├── Model Registry / Downloader / Integrity Checker
   ├── Hardware Profiler & Memory Budget Planner
   └── Local Logs / App Configuration
```

- Use dependency injection and clear interfaces for services; the UI does not know inference CLI syntax.
- Execute CPU-intensive native inference in isolated managed worker processes or safely wrapped native services, with cancellation, timeouts, stdout/stderr parsing, and crash isolation. **Never interpolate user paths into unescaped shell command strings**; use argument arrays and non-shell process launches.
- Workers accept normalized JSON job descriptions, return typed progress/events, and write atomic output files.
- Separate source media, interim audio, raw transcript, human revisions, AI suggestions, and exported documents.
- Persist schema versions with migration strategy. Atomic writes + SQLite transactions. Handle abrupt power loss.
- Feature matrix is defined by provider/runtime/model and used to enable/disable UI controls automatically.

### 12.2 Privacy / security

- **No external transcription/summarization API calls.** Model downloads and explicit update checks are the only permitted network traffic. No analytics/telemetry by default.
- If using a local inference HTTP server, bind to `127.0.0.1` only, allocate an ephemeral port, use process lifetime ownership and reject nonlocal connections. Prefer in-process / named-pipe communication if simpler.
- Protect against path traversal, symlink/junction surprises, ZIP archive extraction attacks, malicious model filenames and untrusted GGUF/ONNX metadata. Imported models are untrusted input.
- Do not auto-run scripts from downloaded model repos; avoid unpinned dependencies with executable code.
- Downloads via HTTPS with pinned expected model repo/revision/hash where feasible. Verify signatures/hashes for bundled runtime executables.
- Model/license notices must be presented and distributable. FFmpeg build configuration and binary licensing (LGPL/GPL where applicable), ONNX Runtime, whisper.cpp, llama.cpp, Moonshine, Silero, denoiser and every model checkpoint should be reviewed for redistribution and commercial-use rights.
- Do not wipe originals, remote folders, source media, or user edits during model or cache cleanup. File deletion actions require scope-specific confirmation.
- Default logs contain only diagnostic metadata; never log transcript bodies, prompts, user media paths when avoidable, or secrets. Provide opt-in diagnostic export with review/redaction.

## 13. Performance requirements and benchmarks

- UI remains responsive during 100% CPU-bound inference; show feedback within approximately 100–200 ms for normal UI actions on target hardware (measure on representative machines).
- Initial app startup should be reasonable and should **not preload any ASR/LLM weights**. Prefer lazy worker startup and fast library navigation.
- Benchmark Parakeet ONNX INT8 vs whisper.cpp Small/Medium/Turbo on identical CPU hardware and audio samples (quiet English, Quebec French, noisy speech, mixed-language, long recording).
- Record **real-time factor** `processing_wall_seconds / audio_duration_seconds`, RAM peak, average CPU usage, time to first segment, disk I/O, WER where ground truth exists, and stability. Avoid unsourced benchmark promises.
- Compare no-enhancement versus enhancement using source-labelled audio; denoising may harm recognition. Select sane default based on measured tradeoffs.
- Long-run stress: 1h, 2h+, multiple sequential jobs, videos >2GB, low disk, removal of workspace, cancellation during download/inference, model-switch loops, long LLM context, Unicode filepaths.
- Optimize algorithmically (streaming decode, bounded chunks, avoiding redundant conversions, batching where supported) before choosing resource-intensive architecture.

## 14. Error handling and user help

Provide clear nontechnical messages and a recovery action for at least:

- No compatible speech model downloaded → `Choose or download a speech model`.
- Missing FFmpeg worker/runtime → bundled dependency installation issue; `Repair installation` if provided, diagnostic error.
- Unsupported or encrypted codec/media → `This file's audio track cannot be decoded` with file/codec details.
- Multiple tracks → explicit track selector.
- Model does not support selected language → automatic reset only with visible notice/confirmation, or choose supported model.
- Insufficient disk → bytes needed / free / change location.
- Not enough memory → smaller model / fewer threads / smaller context suggestion, not process crash.
- Download interrupted → resume or retry, no corrupted installation.
- Stale workspace pointer → locate folder.
- Job interrupted → resume if valid checkpoint, otherwise restart.
- Long transcript exceeds context → automatically offer chunked summary and show the planned number of passes.
- Speaker diarization unavailable → hide option or label as not supported for this setup.
- Imported model incompatible → describe format, missing files or unsupported architecture without trying to execute unknown code.

Tooltips and small `Learn more` sections should explain `speech model`, `language auto-detection`, `timestamps`, `noise reduction`, `context length`, `quantization`, `summarization model`, and `speaker labels` in concise plain language. No onboarding slogans.

## 15. Suggested data contracts

### 15.1 Model registry entry (illustrative)

```json
{
  "id": "parakeet-tdt-v3-onnx-int8",
  "role": "asr",
  "displayName": "Parakeet TDT V3 (CPU, INT8)",
  "publisher": "NVIDIA",
  "runtime": "onnx-asr",
  "artifactSource": "VERIFIED_PINNED_REPOSITORY_AND_REVISION",
  "artifacts": [],
  "license": "VERIFY_WITH_SOURCE_REPOSITORY",
  "capabilities": {
    "languages": ["en", "fr"],
    "languageAutoDetect": true,
    "forcedLanguage": false,
    "segmentTimestamps": true,
    "wordTimestamps": "validate-at-runtime",
    "speakerAttribution": false,
    "promptSupport": false
  },
  "platforms": ["windows-x64-cpu"]
}
```

**Note:** This is a schema *example*, not a ready-to-use download manifest. Replace `artifacts`, repository revision, license, capabilities and requirements with verified release metadata before shipping. Language list shown is intentionally incomplete; registry must use the real supported set.

### 15.2 Workspace item / job (illustrative)

```json
{
  "schemaVersion": 1,
  "jobId": "uuid",
  "workspaceId": "uuid",
  "source": {"path": "C:\\Media\\meeting.mp4", "audioStreamIndex": 1},
  "pipeline": {"denoise": "auto", "vad": "silero", "language": "auto"},
  "asr": {"modelId": "parakeet-tdt-v3-onnx-int8", "modelRevision": "pinned"},
  "state": "transcribing",
  "completedSpeechMs": 600000,
  "sourceDurationMs": 3600000,
  "checkpoints": []
}
```

### 15.3 Transcript segment (illustrative)

```json
{
  "segmentId": "s000128",
  "startMs": 125400,
  "endMs": 131800,
  "text": "We should review the product launch schedule.",
  "speakerId": null,
  "language": "en",
  "words": null,
  "confidence": null,
  "warnings": []
}
```

## 16. Acceptance criteria and test plan

The agent must implement automated tests where possible, plus an end-to-end smoke test with **actual audio** and recorded evidence. Acceptance is based on behavior, not screenshots alone.

### Core acceptance scenarios (P0)

1. **Clean-system install:** On a Windows target without Python/.NET SDK/development tools, extract/install app and launch `LocalTranscriber.exe`; no manual dependency installation; GUI responds without network access.
2. **Multiple workspaces:** Add `C:\Media\Interviews` and `D:\Projects\Meetings`, restart, switch workspaces. Removing pointer to one leaves all media and outputs intact.
3. **Media:** Add WAV/MP3 and MP4/MKV fixtures; ffprobe duration/audio track appears; video audio extracts correctly; corrupt media rejected without crash.
4. **Parakeet:** Download verified Parakeet V3 CPU-compatible artifacts, process an English recording, produce non-empty timestamped transcript. Repeat with French sample.
5. **Whisper:** Download Whisper Medium and Turbo supported models, switch from Parakeet to whisper.cpp with **no GPU** and generate independent transcript versions from same file.
6. **Noisy audio:** `Off`, `Automatic`, and `Enhanced` settings show real chosen processing operations and no destructive change to source. Optional enhancement failure falls back safely.
7. **Long audio:** Process >=60 minutes without reading full decoded media into memory; timestamps refer to source media and restart resumes/recovers from valid checkpoint.
8. **Job UX:** Show real stages, processed/total audio and actual progress. Cancel a job, restart app, verify no zombie worker and no falsely complete result.
9. **Review:** Play recording in-app; click segment to seek near correct moment, search, edit text, undo, save, and reopen edited transcript while raw output remains accessible.
10. **Export:** From same transcript export TXT, MD, JSON, CSV, SRT, VTT; validate UTF-8 French characters and subtitle syntax; timestamp-on/off and speaker option behave accurately.
11. **Summary:** Download LFM2.5-1.2B-Instruct GGUF, generate grounded local summary with custom context/glossary, edit and export Markdown; run again with LFM2.5-2.6B if machine allows.
12. **Context constraint:** A transcript exceeding available context triggers chunked summarize or explicit refusal/warning, never silent truncation or out-of-memory crash.
13. **Models:** Import valid compatible local model, reject incompatible files with explanation; remove unused model and verify model storage shrinks without deleting workspace data.
14. **Hardware:** Device panel displays actual RAM/CPU/free disk and gives model-specific actionable feasibility warnings. CPU-only mode verified; network disconnected during ASR and LLM generation.
15. **UI:** High-DPI light/dark theme, keyboard navigation, accessible control names, no placeholder buttons, dead navigation, fabricated progress, or promotional filler text.

### Additional acceptance scenarios (P1)

- Qwen3-ASR 0.6B / 1.7B speech transcription works on Windows CPU using a real WAV fixture and valid accompanying audio artifacts, or the model is explicitly marked unavailable with documented blocker (not falsely counted as supported).
- Moonshine V2 English works if compatible packaged runtime exists; language gating excludes unverified French checkpoint.
- Speaker labels show `Unknown` unless diarization or human labeling actually assigns them. Speaker edits survive export.
- Model comparison produces independent transcript versions and meaningful time-aligned discrepancy view.
- `Suggest corrections` displays linked proposals; accepting/rejecting preserves raw ASR version and full edit history.
- Model download interruption safely resumes/restarts without corrupt installation; cache cleanup only removes explicitly disposable items.

### Test suite categories

- Unit: timestamp conversions, chunk merging and overlap dedup, CSV quoting, SRT/VTT, registry parsing, memory budget calculations, path validation, prompt/token budgeting.
- Integration: FFmpeg decoding, VAD, ONNX Parakeet, whisper.cpp CPU, llama.cpp LFM2.5, SQLite migrations, model install/remove.
- UI: selection → queue → completion → review → export → summary → export; accessibility and themes.
- Failure injection: process kill, no network, low disk, missing model files, malformed GGUF metadata, locked folders, bad codecs.
- Performance: CPU-only memory and runtime benchmarks, >=60-minute source, 16 GB representative machine and 32 GB machine where available. Clearly separate measured results from untested assumptions.

## 17. Implementation plan for the autonomous agent

**Phase 1 — Environment and architecture:** inventory installed tools, validate GUI framework choice, create repository, interfaces, model registry structure, SQLite schema, package skeleton and tests. Document architecture and constraints.

**Phase 2 — First complete vertical slice:** workspace pointer, import WAV/MP4, FFmpeg conversion, Parakeet ONNX model installation, real transcription, timestamped viewer, TXT/JSON export, packaged EXE that launches. Demonstrate actual result.

**Phase 3 — Provider and output expansion:** whisper.cpp provider, language control, model selection and deletion, Markdown/CSV/SRT/VTT, media seeking, robust jobs/cancellation, handling multi-hour audio.

**Phase 4 — Summary:** llama.cpp + LFM2.5-1.2B/2.6B model manager, local context and glossary, templates, context sizing, chunked summary, Markdown/JSON export, raw-vs-edit history.

**Phase 5 — Enhancements and optional models:** safe denoise + A/B, advanced VAD, Qwen3-ASR, Moonshine, optional aligner/diarizer, 230M/350M/Thinking/8B-A1B model entries, comparison workflows.

**Phase 6 — Packaging and validation:** clean Windows deployment, dependency/legal notices, resource checks, full regression and smoke tests, performance results, user guide, final portable distribution.

Do not stop after Phase 2 or supply only implementation suggestions. Finish as many subsequent phases as feasible. If genuine environmental blockers remain, keep the finished P0 workflow operational and document evidence and exact remaining steps.

## 18. Official sources and engineering references (verified/relevant as of 2026-10-08)

**Windows UI/deployment**

- Microsoft — Windows desktop app packaging overview: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/
- Microsoft — Unpackaged WinUI 3 and self-contained/single-file publishing: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
- Microsoft — WinUI 3: https://learn.microsoft.com/en-us/windows/apps/winui/winui3/

**ASR and audio tools**

- NVIDIA Parakeet TDT V3 official model: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3
- ONNX ASR runtime / VAD segmentation / CPU benchmarks: https://github.com/istupakov/onnx-asr
- whisper.cpp: https://github.com/ggml-org/whisper.cpp
- OpenAI Whisper model repository: https://github.com/openai/whisper
- Qwen3-ASR official implementation and aligner: https://github.com/QwenLM/Qwen3-ASR
- Qwen3-ASR GGUF 0.6B: https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF
- Qwen3-ASR GGUF 1.7B: https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF
- Moonshine V2 Windows support: https://github.com/moonshine-ai/moonshine-v2
- Silero VAD: https://github.com/snakers4/silero-vad
- DeepFilterNet: https://github.com/Rikorose/DeepFilterNet
- FFmpeg: https://ffmpeg.org/

**Local LLM**

- llama.cpp: https://github.com/ggml-org/llama.cpp
- LiquidAI official LFM2.5 collection: https://huggingface.co/collections/LiquidAI/lfm25
- LFM2.5 230M GGUF: https://huggingface.co/LiquidAI/LFM2.5-230M-GGUF
- LFM2.5 350M GGUF: https://huggingface.co/LiquidAI/LFM2.5-350M-GGUF
- LFM2.5 1.2B Instruct GGUF: https://huggingface.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF
- LFM2.5 1.2B Thinking GGUF: https://huggingface.co/LiquidAI/LFM2.5-1.2B-Thinking-GGUF
- LFM2.5 2.6B GGUF: https://huggingface.co/LiquidAI/LFM2.5-2.6B-GGUF
- LFM2.5 8B-A1B GGUF: https://huggingface.co/LiquidAI/LFM2.5-8B-A1B-GGUF

**Source verification policy:** Model availability, runtime flags, license conditions, and operating-system compatibility may change. Review source documentation and pin a known-good release before integrating or distributing. This specification's recommendations are engineering starting points, not verified performance guarantees for every CPU.

---

## 19. Final handoff checklist

- [ ] Runnable Windows self-contained executable / portable release attached
- [ ] Works with no GPU inference and no development runtime installed on target computer
- [ ] Source repository and reproducible build/test scripts
- [ ] Fully functional workspace/media pipeline and at least Parakeet + whisper.cpp CPU models
- [ ] Model downloader/importer/deleter with provenance, licensing and integrity protection
- [ ] Language selection, preprocessing and VAD, progress, cancellation and recovery
- [ ] In-app media/transcript viewer with original + edited versions and correct timestamps
- [ ] TXT, Markdown, CSV, JSON, SRT and VTT transcript exports
- [ ] Local llama.cpp summarization using LiquidAI LFM2.5 with user-provided context
- [ ] Context/window auto-sizing and overflow/chunking strategy; advanced settings
- [ ] Model hardware/memory assessment for 16/32 GB Windows laptops
- [ ] UI consistent with Windows Fluent conventions; accessible, responsive, restrained copy
- [ ] Tested offline after initial downloads; no network calls during processing
- [ ] Test results and honest remaining limitations recorded
- [ ] All third-party licenses and model usage restrictions reviewed and documented

**Definition of done:** A non-technical user on a regular Windows laptop can obtain the application, start it, add a workspace and recording, download a supported model, complete local CPU transcription, review and export the result, select and download a suitable local LFM2.5 model, enter context/terminology, produce and export a useful summary, and later manage disk space by deleting models—all without opening a terminal or connecting to a cloud inference service.
