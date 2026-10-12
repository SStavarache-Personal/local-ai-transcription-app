"""Pinned, isolated CPU worker. No networking, repo code, cloud APIs or telemetry."""
import hashlib
import json
import os
import subprocess
import sys
import wave
from pathlib import Path

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["CUDA_VISIBLE_DEVICES"] = ""

def atomic(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")
    os.replace(temp, path)

def event(stage, seconds=0):
    print(json.dumps({"stage": stage, "completedSeconds": seconds}), flush=True)

def merge(segments):
    """Time-based overlap reconciliation; only deduplicate adjacent overlapping phrases."""
    merged = []
    for segment in sorted(segments, key=lambda s: (s["startMs"], s["endMs"])):
        if not segment["text"].strip():
            continue
        if merged and segment["startMs"] < merged[-1]["endMs"]:
            previous = merged[-1]
            left, right = previous["text"].split(), segment["text"].split()
            for count in range(min(len(left), len(right), 12), 0, -1):
                clean = lambda words: [word.strip(".,!?;:—").casefold() for word in words]
                if clean(left[-count:]) == clean(right[:count]):
                    segment["text"] = " ".join(right[count:])
                    break
            segment["startMs"] = previous["endMs"]
        if segment["text"].strip() and segment["endMs"] > segment["startMs"]:
            segment["segmentId"] = f"s{len(merged) + 1:06d}"
            merged.append(segment)
    return merged

def main(request):
    import numpy as np
    import onnxruntime as rt
    import onnx_asr

    threads = int(request["threads"])
    if not 1 <= threads <= 64:
        raise ValueError("Invalid CPU thread count")
    options = rt.SessionOptions()
    options.intra_op_num_threads = threads
    options.inter_op_num_threads = 1
    options.log_severity_level = 3
    asr = None
    if request["runtime"] == "onnx-asr":
        event("Loading Parakeet CPU model")
        asr = onnx_asr.load_model("nemo-parakeet-tdt-0.6b-v3", request["modelPath"], quantization="int8", sess_options=options, providers=["CPUExecutionProvider"]).with_timestamps()
        if request.get("operation") == "inspect":
            asr.recognize(np.zeros(16000, dtype=np.float32), sample_rate=16000)
            return
    if request.get("operation") == "inspect":
        raise ValueError("Unsupported inspection provider")
    vad_options = rt.SessionOptions()
    vad_options.intra_op_num_threads = 1
    vad_options.log_severity_level = 3
    vad = onnx_asr.load_vad("silero", request["vadPath"], sess_options=vad_options, providers=["CPUExecutionProvider"])
    checkpoint = Path(request["checkpointDirectory"])
    checkpoint.mkdir(parents=True, exist_ok=True)
    signature = hashlib.sha256(json.dumps({k: request[k] for k in ["signature", "runtime", "modelPath", "language", "vadThreshold", "hints"]}, sort_keys=True).encode()).hexdigest()
    plan_file = checkpoint / "plan.json"
    event("Segmenting")
    with wave.open(request["audioPath"], "rb") as audio:
        rate, total = audio.getframerate(), audio.getnframes()
        if audio.getnchannels() != 1 or audio.getsampwidth() != 2 or rate != request["sampleRate"]:
            raise ValueError("Worker requires the provider's mono PCM16 sample rate")
        if plan_file.exists():
            plan_data = json.loads(plan_file.read_text(encoding="utf-8"))
            if plan_data["signature"] != signature:
                raise ValueError("Checkpoint settings changed; restart this job")
            plan = plan_data["regions"]
        else:
            plan = []
            # Bounded 120 s windows: never allocate the complete recording in RAM.
            block = rate * 120
            for base in range(0, total, block):
                audio.setpos(base)
                samples = np.frombuffer(audio.readframes(min(block, total - base)), dtype=np.int16).astype(np.float32) / 32768.0
                regions = next(vad.segment_batch(samples[None, :], np.array([len(samples)], dtype=np.int64), rate,
                              threshold=float(request["vadThreshold"]), max_speech_duration_s=20, speech_pad_ms=200, min_silence_duration_ms=350))
                for start, end in regions:
                    start, end = max(0, base + start), min(total, base + end)
                    # VAD may return a long uninterrupted region; bound it independently.
                    while end - start > rate * 22:
                        plan.append([start, start + rate * 20 + rate // 3])
                        start += rate * 20
                    if end > start:
                        plan.append([start, end])
                event("Segmenting", min(base + block, total) / rate)
            atomic(plan_file, {"signature": signature, "regions": plan})
        segments = []
        for index, (start, end) in enumerate(plan):
            result_file = checkpoint / f"chunk-{index:06d}.json"
            if result_file.exists():
                saved = json.loads(result_file.read_text(encoding="utf-8"))
                if saved["signature"] != signature or saved["range"] != [start, end]:
                    raise ValueError("Checkpoint integrity changed; restart this job")
                segments.extend(saved["segments"])
                event("Transcribing", end / rate)
                continue
            audio.setpos(start)
            frames = audio.readframes(end - start)
            offset = round(start * 1000 / rate)
            chunk_segments = []
            if asr is not None:
                samples = np.frombuffer(frames, dtype=np.int16).astype(np.float32) / 32768.0
                result = asr.recognize(samples, sample_rate=rate)
                if result.text.strip():
                    first = max(0, round(min(result.timestamps or [0]) * 1000))
                    chunk_segments = [{"startMs": offset + first, "endMs": round(end * 1000 / rate), "text": result.text.strip(),
                                       "language": None, "warnings": ["Native token onset; segment end follows the VAD speech boundary. No word alignment or speaker inference."]}]
            else:
                chunk_audio = checkpoint / "current.wav"
                with wave.open(str(chunk_audio), "wb") as output:
                    output.setnchannels(1); output.setsampwidth(2); output.setframerate(rate); output.writeframes(frames)
                native = checkpoint / "current-whisper"
                args = [request["whisperExecutable"], "--no-gpu", "-m", request["modelPath"], "-f", str(chunk_audio), "-t", str(threads), "-l", request["language"], "-oj", "-of", str(native)]
                if request["hints"]:
                    args += ["--prompt", request["hints"]]
                completed = subprocess.run(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW, timeout=3600)
                if completed.returncode:
                    raise RuntimeError("whisper.cpp CPU worker failed")
                result = json.loads(native.with_suffix(".json").read_text(encoding="utf-8-sig"))
                for item in result.get("transcription", []):
                    bounds = item["offsets"]
                    chunk_segments.append({"startMs": offset + int(bounds["from"]), "endMs": min(round(end * 1000 / rate), offset + int(bounds["to"])), "text": item["text"].strip(),
                                           "language": result.get("result", {}).get("language"), "warnings": []})
                chunk_audio.unlink(missing_ok=True)
                native.with_suffix(".json").unlink(missing_ok=True)
            atomic(result_file, {"signature": signature, "range": [start, end], "segments": chunk_segments})
            segments.extend(chunk_segments)
            event("Transcribing", end / rate)
    atomic(request["outputPath"], {"segments": merge(segments)})
    event("Saving", total / rate)

if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "--self-test":
        rows = merge([{"startMs": 0, "endMs": 2000, "text": "Bonjour le monde"}, {"startMs": 1800, "endMs": 4000, "text": "le monde. Ça va?"}])
        assert len(rows) == 2 and rows[1]["text"] == "Ça va?" and rows[1]["startMs"] == 2000
        assert len(merge([{"startMs": 0, "endMs": 2000, "text": "yes"}, {"startMs": 3000, "endMs": 4000, "text": "yes"}])) == 2
        print("Worker merge checks passed.")
    else:
        try:
            main(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig")))
        except Exception as error:
            # Class only: no paths, user speech or prompts in logs.
            print(json.dumps({"error": type(error).__name__}), flush=True)
            sys.exit(1)
