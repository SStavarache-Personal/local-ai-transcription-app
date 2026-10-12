# Test results

This document summarizes historical local prototype checks. Raw JSON reports and screenshots are retained locally and excluded from the public repository because they can contain user paths and recorded content; the filenames below identify that private evidence. New test runs write to ignored `test-output/`. Run `test.ps1 -Smoke` before `python scripts/benchmark_report.py` to generate a fresh report and benchmark in that folder.

## Version 1.2.1 download fix — 2026-10-11 (Toronto)

The installer blocked the compact models' Apache licence URL after their GGUF finished downloading. The approved-source check now permits the exact HTTPS Apache licence host/path while retaining Hugging Face CDN support, rejecting unrelated/insecure/lookalike URLs and enforcing pinned sizes/SHA256. All catalogue artifact URLs are covered by executable checks.

`download-1.2.1-checks.json` records actual HTTPS licence retrieval and atomic installer completion for all five compact-model profiles using small, preverified staged weight fixtures. The fixture URLs deliberately cannot be fetched, proving cached weights were reused. A deliberately incorrect licence hash is rejected and retains the staged weights. `unit-1.2.1-results.json` records 40 self-checks. This change does not alter inference or the user interface. Reproduce the network check with `dotnet run --project tests/download-check -- test-output/download-check.json test-output/download-check-data` while internet access is available. The test uses isolated storage and does not download model weights.

## Version 1.2.0 speech processing — 2026-10-11 (Toronto)

The app passes 38 executable invariants, including migration from 1.1 settings, segment-whitespace normalization before vocabulary replacement, complete system overrides, Markdown permission, plain-text paragraphs/URLs/tables, prompt validation and the five pinned compact-model downloads. `unit-1.2-results.json` records the counted assertions. `prompt-editor-results.json` verifies native WPF add/rename/switch/save/reset/delete behavior; stable profile IDs prevent selection from breaking when mutable editor fields change.

`dictation-1.2-results.json` records real Parakeet and Whisper Small speech inference plus CPU Qwen2.5 7B-Instruct processing. It checks unchanged raw ASR beside final output, separate measured ASR/LLM wall times, saved full-system overrides with an exact-output probe, document output with and without Markdown, and applying another prompt to the same raw speech while preserving its ASR duration and prior processed result. English/French self-corrections and topic paragraphs continue to preserve the selected examples. A local Markdown-conversion microbenchmark is recorded in the report. `ui-1.2-results.json` covers the existing eight-page application workflow with external developer runtime paths disabled. Light/dark comparison, editor and widget captures are in `screenshots/dictation-1.2`.

Removing segment newlines exposed dropped characters in the former rapid Unicode-keyboard path for long single-line results. All insertion now uses the existing confirmed-paste path. `field-1.2-results.json` verifies actual ASR-to-field insertion, Unicode and paragraph preservation, selection replacement, nonactivating widget Record/Cancel clicks, changed-focus/password/read-only rejection, blocked-paste recovery and clipboard restoration. Real microphone buffers are captured and the WAV remains readable after cancellation; this is ambient input, not a human speech-accuracy measurement.

The NuGet vulnerability audit found no vulnerable resolved packages. Markdig 1.4.0 is locked and its BSD-2-Clause licence is bundled. `release-1.2-verification.json` records ZIP extraction, every packaged file's size/SHA256 and the extracted self-contained app's 38 invariants under isolated external runtime paths. The package contains no large downloaded model weights. Timings are stage wall times on this host, including preparation/loading and excluding recording; they are not a broad hardware benchmark.

Remaining checks: clean-machine/Sandbox deployment, Narrator, varied human recordings, third-party/elevated destination controls and 16/32 GB hardware acceptance. Custom system overrides can deliberately change content, so compare important results against raw ASR. Evidence below belongs to earlier releases.

## Version 1.1.0 dictation extension — 2026-10-10 (Toronto)

The packaged application passed 24 counted invariants, the eight-page native UI regression workflow, and `test.ps1 -Dictation` on the same Windows 11 host. `dictation-results.json` records actual Parakeet and Whisper Small audio inference, vocabulary application with immutable raw preservation, and CPU-only Qwen2.5 7B-Instruct editing. English tests cover fillers, dates, names, amounts, and both inline and cross-sentence self-corrections; French preserves the corrected recipient and budget. A two-topic passage produces separate paragraphs while retaining instructions. These are selected examples, not a general accuracy guarantee.

The separate-process test field verifies selection replacement, Unicode accents, multiline paste, actual ASR-to-field insertion, unchanged/changed focus handling, native widget Record/Cancel mouse clicks without focus theft, and rejection of password/read-only controls. A deliberately blocked paste tests the recovery path and clipboard restoration. Single-line insertion uses Unicode packets; paragraphs use clipboard paste with field-text confirmation and sequence-guarded restoration. No send/submit key is generated.

Real Windows microphone input was opened and produced 1.5 seconds of PCM with measurable RMS activity; cancel finalized a probeable WAV and closed the device. This used ambient audio, so human spoken microphone-ASR quality is still unverified. Tutorial completion/replay and persisted settings passed, and light/dark/normal/minimum layouts were rendered and inspected, including the floating widget. Screenshots are in `screenshots/dictation`.

`cleanup-model-probes.json` retains development failures from LFM models so they are not mistaken for reliable dictation editors. Qwen's official two GGUF shards and licence were revision/size/SHA256 pinned, installed separately in normal app storage, and verified after copying. No weights are included in the portable ZIP. The NuGet vulnerability audit found no vulnerable resolved packages on this date. The new code adds no package dependency.

Remaining limits: arbitrary speech may still be misedited; review generated cleanup. Third-party and elevated application compatibility, physical spoken microphone accuracy, Narrator, Windows Sandbox/clean-machine acceptance, and 16/32 GB hardware acceptance have not been established by these checks. Older benchmark and extended recovery evidence below belongs to the initial release.

Tested 2026-10-09 (Toronto) on Windows 11 x64, AMD Ryzen AI 9 HX 370, 12 physical / 24 logical cores, 59,719,397,376 bytes RAM (55.6 GiB). Inference was explicitly CPU-only. The final self-contained desktop runtime is pinned to .NET 10.0.12, the security release identified in [Microsoft's release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json).

## Recorded evidence

- `core-smoke.json`: actual English/French Parakeet V3 INT8 and whisper.cpp Small/Medium/Large V3 Turbo; both LFM2.5 Instruct 1.2B and 2.6B; source hash preservation, timestamp bounds, six exports, edited/raw separation, media probes and corrupt-file rejection.
- `extended-smoke.json`: cancellation after a completed ASR chunk, recovery on a 3616-second source, actual-token map/reduce, isolated GGUF import/removal, interrupted HTTPS artifact download/resume/hash verification and deterministic noisy-audio Off/Automatic/Enhanced processing. Import removal reclaimed approximately 731 MB and left results intact.
- `ui-results.json`: packaged eight-page WPF flow; responsive dispatcher during inference; MP4 playback and segment seeking; actual DataGrid cell edit, Undo/Redo, search, raw/edited switching, revert, persistence, six transcript exports, summary editing/export and generated-original preservation. Light, Dark, System and minimum window size rendered. `screenshots/` contains visual-tree captures with translucent Fluent fills composited over a representative backdrop; native window chrome/video frames are not captured.
- `unit-results.json`: counted executable invariants for Unicode/CSV/subtitle timing, path traversal, bad GGUF, multiple workspace persistence, removal without file deletion, restart recovery, atomic JSON and owned-worker termination. Earlier reports had a hard-coded case count; the final runner counts assertions.
- `benchmark-results.json`: reproducible literal WER and measured wall time/RTF, with available worker RAM/CPU measurements. `python scripts/benchmark_report.py` regenerates it from preserved outputs and owned fixture text.

The native workers and bundled Python were used by absolute path. The packaged UI check ran with PATH restricted to Windows System32 and invalid external DOTNET_ROOT/PYTHONHOME, and still performed actual ASR/LLM inference. This demonstrates independence from the developer tool paths on this host; it does **not** substitute for a clean-machine deployment test.

## Measured short-fixture performance

English duration: 16.3566 s, Windows David synthetic speech. French duration: 12.2379 s, build-only eSpeak NG synthetic speech. Eight inference threads. Cold loading and decoding are included in job wall time; filesystem caches vary between runs.

| Model | English wall / RTF | French wall / RTF | Literal WER English / French |
|---|---:|---:|---:|
| Parakeet V3 INT8 | 5.06 s / 0.309 | 4.32 s / 0.353 | 8.8% / 13.5% |
| Whisper Small | 14.20 s / 0.868 | 4.16 s / 0.340 | 11.8% / 43.2% |
| Whisper Medium | 41.91 s / 2.562 | 11.66 s / 0.953 | 11.8% / 24.3% |
| Whisper Large V3 Turbo | 59.97 s / 3.666 | 13.98 s / 1.142 | 14.7% / 16.2% |

RTF below 1 means faster than the sample's duration. These tiny synthetic samples are not a representative accuracy ranking. The French voice is not a Quebec-accent recording. Literal WER uses NFC/case/punctuation normalization; "$2,000" versus "two thousand dollars" counts as errors. No numeral equivalence, accent removal or optimistic scoring is applied.

Parakeet's Python worker sampled roughly 0.8–0.9 GB working set. The long-source worker remained within the same range. Metrics are sampled process peaks and CPU time, **not total application/model-tree memory**: in particular, Python-parent measurements exclude whisper.cpp children. Complete native-worker/LLM RAM, average CPU, disk I/O and time-to-first-segment comparisons remain to be measured.

The 60-minute fixture places owned speech at the beginning and after 3600 seconds, with silence between. Recovery retained late source offsets and reused checkpoints. This tests duration, bounded windows and recovery; it is not a continuous-speech throughput benchmark. Long summaries used a genuinely overflowing transcript with a 1024-token window and actual llama.cpp tokenization. The 2.6B short smoke intentionally used a 256-output-token limit and surfaced a truncation warning; generation content was nonempty.

## Acceptance status and remaining checks

The functional P0 workflow is implemented and passes the local packaged checks above. These acceptance items remain unverified:

1. **Clean Windows target / disconnected network.** Windows Sandbox is absent on this host, and the session is not elevated. The host network was not disconnected because it carries the remote session. The app has explicit offline/CPU runtime flags and no external inference requests; a fully disconnected run is still required. Run `tests/clean-machine.ps1` on a host with Windows Sandbox installed, then open its generated `.wsb`. Networking and virtual GPU are disabled, models/app/fixtures are mapped read-only, and tests write only to the dedicated evidence folder. The VM requests 16 GB RAM; use `-MemoryInMB 32768` to compare a 32 GB target, on a host with sufficient memory. All six required models must already be installed in the supplied model directory. A successful run writes `PASSED.txt`; failures retain diagnostic reports.
2. **Hardware and stress coverage.** Actual 16/32 GB laptops, older unsupported CPUs, two hours of continuous speech, mixed-language/Quebec-accent human recordings, videos above 2 GB, low-disk and locked/read-only-folder failure injection are not validated. Current memory suggestions are conservative estimates, not promises.
3. **Accessibility/deployment.** Native keyboard/control names and DPI manifest are present. Owned widget clicks and input into a separate-process test field passed in 1.1; compatibility with third-party applications, Narrator, display scaling across monitors, signing/SmartScreen and Windows 10 are not tested.
4. **Optional providers/features.** Qwen3-ASR and Moonshine are visibly unavailable because their audio adapters are not integrated/verified. Automatic diarization, word alignment, discrepancy highlighting and correction proposals are not implemented. Manual speakers, independent runs, glossary import/export, batch jobs, A/B previews and chunked summaries are implemented. Downloadable model entries describe capabilities, provenance and resource needs.
5. **External redistribution.** Bundled notices and model restrictions were collected. This local development package is not a completed public distribution/legal audit: FFmpeg corresponding source/build material and applicable Microsoft CRT redistribution rights need to accompany any external release. See THIRD_PARTY_NOTICES.md.

NuGet's vulnerability audit reported no vulnerable resolved packages on this date after the SQLite bundle update. Dependency versions, wheel/archive/model hashes and licence sources are locked; the release includes a per-file SHA256 manifest and a separate ZIP checksum. Generated model text still requires human review for factual errors and omissions.
