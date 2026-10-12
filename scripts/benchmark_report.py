"""Report literal word error rate on owned synthetic fixtures; no quality claims beyond these samples."""
import json
import re
import unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def words(text):
    return re.findall(r"\w+(?:['’]\w+)*", unicodedata.normalize("NFC", text).casefold())

def distance(reference, actual):
    previous = list(range(len(actual) + 1))
    for i, left in enumerate(reference, 1):
        current = [i]
        for j, right in enumerate(actual, 1):
            current.append(min(current[-1] + 1, previous[j] + 1, previous[j - 1] + (left != right)))
        previous = current
    return previous[-1]

def main():
    source = ROOT / "test-output/test-results.json"
    evidence = json.loads(source.read_text(encoding="utf-8-sig"))
    rows = []
    for check in evidence["evidence"]:
        if not check["check"].startswith("ASR "):
            continue
        fixture = check["check"].rsplit(" ", 1)[-1].replace(".wav", ".txt")
        reference = words((ROOT / "tests/fixtures" / fixture).read_text(encoding="utf-8"))
        transcript = json.loads(Path(check["raw"]).read_text(encoding="utf-8"))
        actual = words(" ".join(s["text"] for s in transcript["segments"]))
        edits = distance(reference, actual)
        metrics = check.get("workerMetrics", [])
        rows.append({"model": transcript["modelId"], "fixture": fixture, "wallSeconds": check["wallSeconds"],
                     "audioSeconds": check["audioSeconds"], "realTimeFactor": check["realTimeFactor"],
                     "literalWordEdits": edits, "referenceWords": len(reference), "literalWer": edits / len(reference),
                     "workerMetrics": metrics})
    result = {"hardware": evidence["hardware"], "ramBytes": evidence["ram"], "results": rows,
              "limits": "Short synthetic speech only. English: Windows David; French: eSpeak NG, not a Quebec accent. NFC/case/punctuation normalization; numerals versus spoken numbers count as word errors. Worker RAM is sampled, excludes other children/app, and CPU seconds are per worker. Not a representative accuracy or sustained-throughput benchmark."}
    (source.parent / "benchmark-results.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    for row in rows:
        print(f"{row['model']:24} {row['fixture']:12} {row['wallSeconds']:6.2f}s RTF {row['realTimeFactor']:.3f} literal WER {row['literalWer']:.1%}")

if __name__ == "__main__":
    assert distance(words("Bonjour Québec"), words("bonjour Québec!")) == 0
    assert distance(["a", "b"], ["a", "c"]) == 1
    main()
