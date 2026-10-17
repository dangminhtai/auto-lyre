"""Local adapter for the installed Transkun 2.0.1 pretrained model (MIT).

Upstream: https://github.com/Yujia-Yan/Transkun
No key transposition or quantization is performed here; Auto Lyre plans playback.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import wave


def status(stage):
    print(json.dumps({"stage": stage}), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("output")
    args = parser.parse_args()
    source, output = Path(args.source).resolve(), Path(args.output).resolve()
    if not source.is_file() or source == output:
        raise ValueError("Invalid source/output paths")
    status("loading")
    import imageio_ffmpeg
    import moduleconf
    import numpy as np
    import torch
    from importlib.resources import files
    from transkun.Data import writeMidi

    torch.set_num_threads(min(4, os.cpu_count() or 1))
    torch.set_grad_enabled(False)
    resources = files("transkun") / "pretrained"
    config = moduleconf.parseFromFile(str(resources / "2.0.conf"))["Model"]
    model = config.module.TransKun(conf=config.config).to("cpu")
    checkpoint = torch.load(str(resources / "2.0.pt"), map_location="cpu", weights_only=False)
    model.load_state_dict(checkpoint.get("best_state_dict", checkpoint.get("state_dict")), strict=True)
    model.eval()
    status("decoding")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="decode-", dir=output.parent) as temp:
        wav = Path(temp) / "decoded.wav"
        result = subprocess.run([
            imageio_ffmpeg.get_ffmpeg_exe(), "-nostdin", "-v", "error", "-y",
            "-i", str(source), "-vn", "-ac", "1", "-ar", str(model.fs),
            "-c:a", "pcm_s16le", str(wav)
        ], capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        if result.returncode:
            raise ValueError("Audio decoding failed: " + result.stderr.decode("utf-8", errors="replace")[-2000:])
        with wave.open(str(wav), "rb") as audio:
            samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768.0
        if samples.size == 0:
            raise ValueError("Audio is empty")
        status("transcribing")
        # Use the pretrained model's native overlap/merge policy; do not split MIDI by hand.
        with torch.no_grad():
            notes = model.transcribe(torch.from_numpy(samples[:, None]), discardSecondHalf=False)
        status("writing")
        partial = output.with_suffix(".partial.mid")
        writeMidi(notes).write(str(partial))
        os.replace(partial, output)
    status("done")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
