# Third-party notices

This local development distribution includes bundled runtimes and a small Silero VAD weight file. Large ASR/LLM weights are **not** redistributed inside the portable app; they download separately when requested. See `catalogue.json`, `runtime-lock.json`, `bundle-manifest.json`, the NuGet lockfile and Python requirements for exact artifact identities, versions, sizes and hashes.

| Component | Licence / source | Distribution notes |
|---|---|---|
| .NET 10.0.12 / WPF | MIT, [dotnet](https://github.com/dotnet) | Pinned self-contained desktop runtime. Runtime and WPF licence and third-party notices are included in licences/. |
| Microsoft.Data.Sqlite / SQLitePCLRaw | MIT / Apache-2.0, [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | SQLite core is public domain. The bundle pins a fixed native version instead of the vulnerable initial dependency. |
| Markdig 1.4.0 | BSD-2-Clause, [source](https://github.com/xoofx/markdig/tree/56e9c238584a44a169f174c881855c049768634c) | Local Markdown-to-plain-text processing. Exact package integrity is in the NuGet lockfile; licence included in licences/. |
| whisper.cpp b5454 | MIT, [source](https://github.com/ggml-org/whisper.cpp/tree/b5454) | CPU Windows binary, upstream source and notices. |
| llama.cpp b11539 | MIT, [source](https://github.com/ggml-org/llama.cpp/tree/b11539) | CPU Windows binary. Models retain separate licences. |
| CPython 3.13.9 | PSF-2.0, [source](https://www.python.org/downloads/release/python-3139/) | Isolated embeddable interpreter; preserve LICENSE.txt. |
| onnx-asr 0.10.2 | MIT, [source](https://github.com/istupakov/onnx-asr) | Data-only local model loading, no downloaded code. |
| ONNX Runtime CPU 1.31.0 | MIT, [source](https://github.com/microsoft/onnxruntime) | Bundled wheel includes native binaries and third-party notices. |
| NumPy 2.5.3 | BSD-3-Clause, [source](https://github.com/numpy/numpy) | Preserve wheel licences, including bundled native components. |
| Flatbuffers / Protobuf / Packaging | Apache-2.0 / BSD-3-Clause / Apache-2.0 and BSD-2-Clause | Preserve all wheel dist-info licence material. |
| Silero VAD ONNX | MIT, [Silero](https://github.com/snakers4/silero-vad), [conversion](https://huggingface.co/onnx-community/silero-vad) | Pinned 2.24 MB VAD model, conversion revision in runtime lock. |
| FFmpeg (Gyan essentials) | GPL-3.0-or-later, [build supplier](https://www.gyan.dev/ffmpeg/builds/), [FFmpeg legal](https://ffmpeg.org/legal.html) | Build includes GPL components. The original package LICENSE and build README are bundled. Source/build availability must accompany any external redistribution. |
| Microsoft Visual C++ runtime | Microsoft redistributable terms, [documentation](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files), [Community redistribution list](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution) | App-local DLLs support native workers; the builder must hold appropriate redistribution rights. A valid Visual Studio Community licence can qualify; a paid edition is not required. Preserve the Microsoft files unmodified and comply with the applicable licence terms. |
| Parakeet TDT V3 / community ONNX conversion | CC-BY-4.0, [NVIDIA](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3), [conversion](https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx) | Attribute NVIDIA and conversion author; do not imply official NVIDIA packaging. |
| Whisper model conversions | MIT, [OpenAI](https://github.com/openai/whisper), [conversion](https://huggingface.co/ggerganov/whisper.cpp) | Separate downloaded assets. |
| Qwen2.5 7B-Instruct GGUF | Apache-2.0, [official model](https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF) | Optional local dictation cleanup model. Both GGUF shards and the licence are revision/size/SHA256 pinned. Weights download separately; licence also included in assets/licences. |
| Qwen3.5 0.8B / 2B / 4B | Apache-2.0, [Qwen upstream](https://huggingface.co/Qwen), [Unsloth conversions](https://huggingface.co/unsloth) | Text-only Q4_K_M downloads; each GGUF and licence is revision/size/SHA256 pinned in catalogue.json. |
| Gemma 4 E2B / E4B | Apache-2.0, [Google upstream](https://huggingface.co/google), [Unsloth conversions](https://huggingface.co/unsloth) | Text-only Q4_K_M downloads; each GGUF and licence is revision/size/SHA256 pinned in catalogue.json. |
| LiquidAI LFM2.5 weights | **LFM Open License v1.0**, [official collection](https://huggingface.co/collections/LiquidAI/lfm25) | Licence downloaded alongside each official model. Commercial rights are limited by an annual revenue threshold of US$10 million; consult sections 1 and 5 of the bundled licence and obtain a suitable licence where required. Redistribution requires preserving the licence and applicable attribution/notices. |

Synthetic test recordings use application-owned text rendered by Windows David (English) and build-only eSpeak NG (French). eSpeak NG is not bundled in the application. No third-party recordings or large model weights are included in the app ZIP.

The application source is GPL-3.0-or-later. This aligns the application release with the bundled GPL FFmpeg build, but does not replace third-party obligations. **This is a local development release, not a legal certification for public redistribution.** Before publishing externally, supply the complete corresponding source/build material for FFmpeg and its enabled dependencies, preserve native runtime notices, and verify Microsoft CRT redistribution terms and the separately downloaded model licences. Sources and licence text are documented and collected by `scripts/collect_notices.py`.
