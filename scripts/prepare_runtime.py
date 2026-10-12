"""Build-only downloads. Verify locked archive hashes and reject unsafe ZIP members."""
import concurrent.futures
import hashlib
import json
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / ".tools"

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def unpack(entry):
    archive = TOOLS / (entry["id"] + ".zip")
    if not archive.exists() or digest(archive) != entry["sha256"]:
        part = archive.with_suffix(".partial")
        print("Downloading", entry["id"], flush=True)
        request = urllib.request.Request(entry["url"], headers={"User-Agent": "LocalTranscriber-build"})
        with urllib.request.urlopen(request, timeout=180) as response, part.open("wb") as output:
            shutil.copyfileobj(response, output, 1024 * 1024)
        if digest(part) != entry["sha256"]:
            raise ValueError("Runtime integrity check failed: " + entry["id"])
        part.replace(archive)
    destination = TOOLS / "runtime" / entry["id"]
    destination.mkdir(parents=True, exist_ok=True)
    if entry.get("kind") == "file":
        shutil.copy2(archive, destination / entry["filename"])
        return destination
    with zipfile.ZipFile(archive) as package:
        for member in package.infolist():
            path = (destination / member.filename).resolve()
            if not path.is_relative_to(destination.resolve()) or (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Unsafe runtime archive member")
        package.extractall(destination)
    return destination

def main():
    TOOLS.mkdir(exist_ok=True)
    entries = json.loads((ROOT / "assets/runtime-lock.json").read_text())
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        list(pool.map(unpack, entries))
    python = TOOLS / "runtime/python"
    (python / "python313._pth").write_text("python313.zip\n.\nLib/site-packages\nimport site\n")
    site = python / "Lib/site-packages"
    requirements = ROOT / "workers/requirements.lock.txt"
    receipt = python / "requirements.sha256"
    if not receipt.exists() or receipt.read_text() != digest(requirements):
        subprocess.run([sys.executable, "-m", "pip", "install", "--require-hashes", "--upgrade", "--only-binary=:all:", "--no-compile", "--target", str(site), "-r", str(requirements)], check=True)
        receipt.write_text(digest(requirements))
    # Preserve licences and FFmpeg's own build information in the portable distribution.
    media = TOOLS / "runtime/ffmpeg"
    for name in ["ffmpeg.exe", "ffprobe.exe"]:
        binary = next(media.rglob(name))
        if binary.parent != media:
            shutil.copy2(binary, media / name)
    print("Runtime bundle ready.", flush=True)

if __name__ == "__main__":
    main()
