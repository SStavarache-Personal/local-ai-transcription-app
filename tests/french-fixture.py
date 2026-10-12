"""Own test text, offline French synthesis. Build/test-only espeakng-loader 0.2.4."""
import ctypes
import wave
from pathlib import Path
import espeakng_loader

text = "Bonjour et bienvenue à la réunion du projet Local Transcriber. Nous avons décidé de tester cette application vendredi. Marie va vérifier la transcription française. Le budget est de deux mille dollars. Il faut conserver le fichier original."
library = espeakng_loader.load_library()
library.espeak_Initialize.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_char_p, ctypes.c_int]
rate = library.espeak_Initialize(2, 0, str(Path(espeakng_loader.get_data_path()).parent).encode(), 0)
if rate <= 0:
    raise RuntimeError("eSpeak initialization failed")
buffers = []
callback_type = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.POINTER(ctypes.c_short), ctypes.c_int, ctypes.c_void_p)
@callback_type
def collect(samples, length, events):
    if samples and length:
        buffers.append(ctypes.string_at(samples, length * 2))
    return 0
library.espeak_SetSynthCallback(collect)
library.espeak_SetVoiceByName.argtypes = [ctypes.c_char_p]
assert library.espeak_SetVoiceByName(b"fr") == 0
encoded = text.encode("utf-8")
library.espeak_Synth.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_int, ctypes.c_uint, ctypes.c_uint, ctypes.c_void_p, ctypes.c_void_p]
assert library.espeak_Synth(encoded, len(encoded) + 1, 0, 0, 0, 1, None, None) == 0
library.espeak_Synchronize()
root = Path(__file__).parent / "fixtures"
root.mkdir(exist_ok=True)
with wave.open(str(root / "french.wav"), "wb") as output:
    output.setnchannels(1); output.setsampwidth(2); output.setframerate(rate); output.writeframes(b"".join(buffers))
(root / "french.txt").write_text(text, encoding="utf-8")
print("Generated offline French fixture with eSpeak NG.")
