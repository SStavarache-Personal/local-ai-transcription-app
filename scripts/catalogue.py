"""Explicit, build-time catalogue refresh. The app never calls this on startup."""
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "LocalTranscriber-build"}), timeout=60) as response:
        return response.read()

def model(repo, identity, name, runtime, files, languages, memory, description, license_name):
    info = json.loads(get(f"https://huggingface.co/api/models/{repo}?blobs=true"))
    revision = info["sha"]
    artifacts = []
    for name_in_repo in files:
        file = next(x for x in info["siblings"] if x["rfilename"] == name_in_repo)
        url = f"https://huggingface.co/{repo}/resolve/{revision}/{name_in_repo}"
        artifacts.append({"name": name_in_repo, "url": url, "size": file["size"],
                          "sha256": file.get("lfs", {}).get("sha256") or hashlib.sha256(get(url)).hexdigest()})
    return {"id": identity, "name": name, "role": "llm" if runtime == "llama.cpp" else "asr",
            "runtime": runtime, "revision": revision, "license": license_name,
            "source": f"https://huggingface.co/{repo}", "description": description,
            "languages": languages, "forcedLanguage": runtime == "whisper.cpp",
            "sampleRate": 16000, "estimatedMemoryMb": memory, "available": True, "artifacts": artifacts}

def compact_models():
    license_url = "https://www.apache.org/licenses/LICENSE-2.0.txt"
    license_text = get(license_url)
    (ROOT / "assets/licences/Apache-2.0.txt").write_bytes(license_text)
    license_artifact = {"name": "LICENSE", "url": license_url, "size": len(license_text), "sha256": hashlib.sha256(license_text).hexdigest()}
    entries = []
    variants = [("Qwen3.5-0.8B", "qwen35-0.8b", 3000), ("Qwen3.5-2B", "qwen35-2b", 4500), ("Qwen3.5-4B", "qwen35-4b", 7000), ("gemma-4-E2B-it", "gemma4-e2b", 8000), ("gemma-4-E4B-it", "gemma4-e4b", 12000)]
    for variant, identity, memory in variants:
        entry = model(f"unsloth/{variant}-GGUF", identity, variant.replace("gemma-4-", "Gemma 4 ").replace("-it", "") + " · Q4_K_M", "llama.cpp", [variant + "-Q4_K_M.gguf"], ["en", "fr"], memory,
                      "Local text processing and summaries. Upstream Qwen/Google model; Unsloth GGUF conversion. Text input only; CPU inference. RAM estimate includes context and runtime overhead, not just weight size.", "Apache-2.0")
        entry["artifacts"].append(license_artifact.copy())
        entries.append(entry)
    return entries

def main():
    parakeet_languages = ["bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de", "el", "hu", "it", "lv", "lt", "mt", "pl", "pt", "ro", "sk", "sl", "es", "sv", "ru", "uk"]
    # Verified Whisper languages; keep the UI's choices limited to this published subset.
    whisper_languages = ["en", "fr", "ar", "zh", "cs", "da", "nl", "fi", "de", "el", "he", "hi", "hu", "id", "it", "ja", "ko", "no", "pl", "pt", "ro", "ru", "es", "sv", "tr", "uk", "vi"]
    entries = [model("istupakov/parakeet-tdt-0.6b-v3-onnx", "parakeet-v3-int8", "Parakeet TDT V3 · INT8", "onnx-asr",
                     ["config.json", "vocab.txt", "encoder-model.int8.onnx", "decoder_joint-model.int8.onnx"],
                     parakeet_languages, 3500, "European languages including English and French. Automatic language selection; CPU only. NVIDIA model, community ONNX conversion.", "CC-BY-4.0")]
    for variant, name, memory in [("tiny", "Tiny", 1000), ("small", "Small", 2500), ("medium", "Medium", 5000), ("large-v3-turbo", "Large V3 Turbo", 6000), ("large-v3", "Large V3", 10000)]:
        entries.append(model("ggerganov/whisper.cpp", f"whisper-{variant}", f"Whisper {name}", "whisper.cpp", [f"ggml-{variant}.bin"], whisper_languages, memory,
                             "Multilingual alternative. Larger models take more CPU time. Native whisper.cpp weights; no GPU required.", "MIT"))
    for variant, memory in [("230M", 1500), ("350M", 1500), ("1.2B-Instruct", 3000), ("1.2B-Thinking", 3500), ("2.6B", 5000), ("8B-A1B", 10000)]:
        try:
            entries.append(model(f"LiquidAI/LFM2.5-{variant}-GGUF", f"lfm25-{variant.lower()}", f"LFM2.5 {variant} · Q4_K_M", "llama.cpp", [f"LFM2.5-{variant}-Q4_K_M.gguf", "LICENSE"],
                                 ["en", "fr"], memory, "Local text summaries. RAM estimate includes runtime overhead; context is capped conservatively.", "LFM Open License v1.0"))
        except (urllib.error.HTTPError, StopIteration) as error:
            entries.append({"id": f"lfm25-{variant.lower()}", "name": f"LFM2.5 {variant}", "role": "llm", "runtime": "llama.cpp", "available": False,
                            "source": f"https://huggingface.co/LiquidAI/LFM2.5-{variant}-GGUF", "blocker": f"The specified official Q4_K_M artifact was unavailable during verification: {type(error).__name__}."})
    entries.append(model("Qwen/Qwen2.5-7B-Instruct-GGUF", "qwen25-7b-instruct", "Qwen2.5 7B-Instruct · Q4_K_M", "llama.cpp",
                         ["qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf", "qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf", "LICENSE"],
                         ["en", "fr"], 8500, "Local dictation cleanup and summaries. Two GGUF shards, verified together. CPU only; allow additional processing time for faithful editing.", "Apache-2.0"))
    entries.extend(compact_models())
    for name in ["Qwen3-ASR 0.6B", "Qwen3-ASR 1.7B", "Moonshine V2 English"]:
        entries.append({"id": name.lower().replace(" ", "-"), "name": name, "runtime": "unavailable", "role": "asr", "available": False,
                        "blocker": "No audio-capable Windows CPU adapter has passed this release's smoke test. This model cannot be downloaded or selected here."})
    (ROOT / "assets/catalogue.json").write_text(json.dumps(entries, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Pinned {sum(x['available'] for x in entries)} available model variants; {sum(not x['available'] for x in entries)} explicitly unavailable.")

if __name__ == "__main__":
    if "--compact-only" in sys.argv:
        additions = compact_models()
        path = ROOT / "assets/catalogue.json"
        existing = json.loads(path.read_text(encoding="utf-8"))
        ids = {entry["id"] for entry in additions}
        path.write_text(json.dumps([entry for entry in existing if entry["id"] not in ids] + additions, indent=2, ensure_ascii=False), encoding="utf-8")
        print("Pinned five compact model downloads; existing model revisions retained.")
    else:
        main()
