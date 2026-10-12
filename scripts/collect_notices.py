"""Collect upstream licence text into the portable bundle; explicit build-time network access."""
import json
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sources = {
    "dotnet-MIT.txt": "https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/LICENSE.TXT",
    "dotnet-THIRD-PARTY-NOTICES.txt": "https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/THIRD-PARTY-NOTICES.TXT",
    "WPF-MIT.txt": "https://raw.githubusercontent.com/dotnet/wpf/v10.0.12/LICENSE.TXT",
    "WPF-THIRD-PARTY-NOTICES.txt": "https://raw.githubusercontent.com/dotnet/wpf/v10.0.12/THIRD-PARTY-NOTICES.TXT",
    "SQLitePCLRaw-Apache-2.0.txt": "https://www.apache.org/licenses/LICENSE-2.0.txt",
    "whisper-MIT.txt": "https://raw.githubusercontent.com/ggml-org/whisper.cpp/b5454/LICENSE",
    "llama-MIT.txt": "https://raw.githubusercontent.com/ggml-org/llama.cpp/b11539/LICENSE",
    "Silero-MIT.txt": "https://huggingface.co/onnx-community/silero-vad/resolve/e71cae966052b992a7eca6b17738916ce0eca4ec/LICENSE",
    "Parakeet-CC-BY-4.0.txt": "https://creativecommons.org/licenses/by/4.0/legalcode.txt",
    "LFM-License-1.0.txt": "https://huggingface.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF/resolve/8ed288026e23958ad9dfa92d53ed773a8eee7125/LICENSE",
}
destination = ROOT / "assets/licences"
destination.mkdir(exist_ok=True)
for filename, url in sources.items():
    request = urllib.request.Request(url, headers={"User-Agent": "LocalTranscriber-build"})
    with urllib.request.urlopen(request, timeout=60) as source:
        (destination / filename).write_bytes(source.read())
(destination / "sources.json").write_text(json.dumps(sources, indent=2), encoding="utf-8")
print("Upstream licence text collected.")
