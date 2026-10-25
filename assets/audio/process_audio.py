"""Denoise, cut, loop and loudness-balance every bike's engine clips.

Run from the repo root:  ./.venv/bin/python assets/audio/source/process_audio.py
Inputs (assets/audio/source, not in the repository; see CREDITS.md): a Fat Boy start+idle recording and a ride recording, the Shovelhead start file,
and one Freesound preview (shut-off).
Outputs: assets/audio/<bike>/*.wav for every bike in ROSTER, assets/audio/fatboy (the untouched base set),
assets/audio/shared/shutoff.wav, demos/*.m4a sources (wav, encoded by the caller), levels.json, manifest.json.
"""
import os, json, numpy as np, soundfile as sf, noisereduce as nr, pyloudnorm as pyln
from fractions import Fraction
from scipy.signal import butter, sosfiltfilt, fftconvolve, resample_poly

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = ROOT + "/source"
SR = 44100
PEAK = 10 ** (-1 / 20)          # -1 dBFS ceiling
TARGETS = {"startup_short": -22, "startup_full": -22, "idle_loop": -24, "rev_blips": -17, "accel": -15, "cruise_high": -18, "cruise_fast": -15, "shutoff": -22}

# The roster: one engine set per bike with its own model. speed: the Fat Boy cruise/accel/blips/shut-off clips are
# resampled to this speed. The Wraith and Draugr Cycles share the Hel Cycle's bundle and so its sounds.
ROSTER = {
    "hel": dict(label="Hel Cycle", era="Shovelhead (own start file)", speed=0.80),
}

def load_mono(path):
    y, sr = sf.read(path, dtype="float32", always_2d=True)
    assert sr == SR, (path, sr)
    return y.mean(1)

def highpass(y, hz=35): return sosfiltfilt(butter(2, hz, btype="high", fs=SR, output="sos"), y).astype(np.float32)
def lowpass(y, hz=9000): return sosfiltfilt(butter(2, hz, btype="low", fs=SR, output="sos"), y).astype(np.float32)

def make_loop(m, s_lo, s_hi, Lmin, Lmax, W):
    """Seamless loop: steadiest start in [s_lo,s_hi]; end chosen so the W s before it
    match the W s before the start; last W s cross-faded into the pre-start audio."""
    best = None
    for s0 in np.arange(s_lo, s_hi, 0.25):
        a = int(s0 * SR); seg = m[a:a + int(Lmin * SR)]
        fr = seg[: len(seg) // (SR // 10) * (SR // 10)].reshape(-1, SR // 10)
        v = np.sqrt((fr ** 2).mean(1)); score = v.std() / (v.mean() + 1e-9)
        if best is None or score < best[0]: best = (score, s0)
    s0 = best[1]; a = int(s0 * SR); Wn = int(W * SR)
    ref = m[a - Wn:a]; ref = (ref - ref.mean()) / (ref.std() + 1e-9)
    lo, hi = a + int(Lmin * SR), a + int(Lmax * SR); region = m[lo - Wn:hi]
    corr = fftconvolve(region, ref[::-1], mode="valid")
    e2 = fftconvolve(region ** 2, np.ones(Wn), mode="valid"); ncorr = corr / (np.sqrt(e2 * Wn) + 1e-9)
    k = int(np.argmax(ncorr)); e = lo - Wn + k + Wn
    loop = m[a:e].copy(); t = np.linspace(0, 1, Wn, dtype=np.float32)
    loop[-Wn:] = loop[-Wn:] * np.cos(t * np.pi / 2) + m[a - Wn:a] * np.sin(t * np.pi / 2)
    return loop, s0, e / SR, float(ncorr[k])

def cut(m, t0, t1, fin=0.01, fout=0.01):
    seg = m[int(t0 * SR):int(t1 * SR)].copy(); a, b = int(fin * SR), int(fout * SR)
    if a: seg[:a] *= np.linspace(0, 1, a, dtype=np.float32)
    if b: seg[-b:] *= np.linspace(1, 0, b, dtype=np.float32)
    return seg

def respeed(y, speed):
    """Play at `speed` x (0.8 = slower and lower). Loops stay seamless because the whole loop is stretched the same way."""
    if abs(speed - 1.0) < 1e-6: return y.astype(np.float32)
    f = Fraction(speed).limit_denominator(100)          # speed = num/den  ->  up by den, down by num
    return resample_poly(y, f.denominator, f.numerator).astype(np.float32)

meter = pyln.Meter(SR)
def lufs(y): return float(meter.integrated_loudness(y.astype(np.float64)))

def normalise(y, target_lufs):
    g = 10 ** ((target_lufs - lufs(y)) / 20); y = y * g; pk = np.abs(y).max(); note = ""
    if pk > PEAK: y = y * (PEAK / pk); note = f"peak-limited, {20*np.log10(pk/PEAK):.1f} dB short of target"
    return y.astype(np.float32), note

def write(path, y):
    os.makedirs(os.path.dirname(path), exist_ok=True); sf.write(path, np.clip(y, -1, 1), SR, subtype="PCM_16")

log = {}
def finish(bike, clips):
    """Loudness-balance a bike's clips, write them, log them. Returns the balanced arrays."""
    out = {}
    for name, y in clips.items():
        before = lufs(y); z, note = normalise(y, TARGETS[name]); out[name] = z
        write(f"{ROOT}/{bike}/{name}.wav", z)
        log.setdefault(bike, {})[name] = {**log.get(bike, {}).get(name, {}), "seconds": round(len(z) / SR, 2), "lufs_in": round(before, 1), "lufs_out": round(lufs(z), 1), "target": TARGETS[name], "note": note}
        print(f"{bike + '/' + name:24s} {len(z)/SR:6.2f}s  {before:6.1f} -> {lufs(z):6.1f} LUFS (target {TARGETS[name]}){'  ' + note if note else ''}")
    return out

# ---------- Fat Boy base set: the start-up + idle recording and the Fat Boy 114 ride ----------
raw_fb = load_mono(SRC + "/fatboy_startup_idle.mp3")
fb = nr.reduce_noise(y=raw_fb, sr=SR, y_noise=raw_fb[: int(1.15 * SR)], stationary=True, prop_decrease=0.85, n_fft=2048)
fb = highpass(fb, 30)
idle, s0, e, q = make_loop(fb, 20, 24, 6, 12, 0.3)
log.setdefault("fatboy", {})["idle_loop"] = dict(src="fatboy startup+idle", start=round(s0, 2), end=round(e, 2), seam=round(q, 3))
raw_ride = load_mono(SRC + "/fatboy_114_ride.mp3")
ride = nr.reduce_noise(y=highpass(raw_ride, 35), sr=SR, stationary=True, prop_decrease=0.45, n_std_thresh_stationary=1.2, n_fft=2048)
cruise_high, s0h, eh, qh = make_loop(ride, 195, 200, 8, 12, 1.0)
cruise_fast, s0f, ef, qf = make_loop(ride, 180, 184, 5, 8, 1.0)
log["fatboy"]["cruise_high"] = dict(src="fatboy 114 ride", start=round(s0h, 2), end=round(eh, 2), seam=round(qh, 3))
log["fatboy"]["cruise_fast"] = dict(src="fatboy 114 ride", start=round(s0f, 2), end=round(ef, 2), seam=round(qf, 3))
sp = load_mono(SRC + "/freesound_previews/502690_CC0_sportster_start_idle_revs_shutoff_demodave.mp3")
sp = nr.reduce_noise(y=sp, sr=SR, y_noise=sp[int(161.0 * SR):int(161.8 * SR)], stationary=True, prop_decrease=0.7, n_fft=2048)
base = finish("fatboy", {"startup_short": cut(fb, 1.2, 5.5, fin=0.005, fout=0), "startup_full": cut(fb, 1.2, s0, fin=0.005, fout=0),
                         "idle_loop": idle, "rev_blips": cut(fb, 76.2, 80.8), "accel": cut(ride, 127.5, 134.5, fin=0.02, fout=0.15),
                         "cruise_high": cruise_high, "cruise_fast": cruise_fast, "shutoff": cut(highpass(sp, 30), 156.0, 161.5, fout=0)})
write(f"{ROOT}/shared/shutoff.wav", base["shutoff"])

# ---------- Shovelhead start file: the Hel Cycle's start-up and idle ----------
raw_sh = load_mono(SRC + "/shovelhead_start_idle.mp3")
sh = nr.reduce_noise(y=raw_sh, sr=SR, y_noise=raw_sh[int(0.5 * SR):int(5.0 * SR)], stationary=True, prop_decrease=0.92, n_fft=4096)
sh = lowpass(highpass(sh, 30), 9000)
sh_idle, sh_s0, sh_e, sh_q = make_loop(sh, 26, 30, 5, 8, 0.3)
log.setdefault("hel", {})["idle_loop"] = dict(src="shovel start+idle (cleaned)", start=round(sh_s0, 2), end=round(sh_e, 2), seam=round(sh_q, 3))

for bike, spec in ROSTER.items():
    speed = spec["speed"]
    loop = sh_idle                                               # the Hel Cycle's own Shovelhead start-up and idle
    startup_full = cut(sh, 5.5, sh_s0, fin=0.005, fout=0)
    startup_short = cut(sh, 9.3, 13.2, fin=0.005, fout=0.05)
    clips = {"startup_short": startup_short, "startup_full": startup_full, "idle_loop": loop,
             "rev_blips": respeed(base["rev_blips"], speed), "accel": respeed(base["accel"], speed),
             "cruise_high": respeed(base["cruise_high"], speed), "cruise_fast": respeed(base["cruise_fast"], speed),
             "shutoff": respeed(base["shutoff"], speed)}
    out = finish(bike, clips)
    log[bike]["speed"] = speed; log[bike]["era"] = spec["era"]
    # demo: start, idle x2, blips, idle, accel, cruise high x2, cruise fast x2, idle, shut-off
    seq = [("startup", out["startup_short"]), ("idle", out["idle_loop"]), ("idle", out["idle_loop"]), ("blips", out["rev_blips"]), ("idle", out["idle_loop"]),
           ("accel", out["accel"]), ("cruise_high", out["cruise_high"]), ("cruise_high", out["cruise_high"]), ("cruise_fast", out["cruise_fast"]),
           ("cruise_fast", out["cruise_fast"]), ("idle", out["idle_loop"]), ("shutoff", out["shutoff"])]
    demo = np.zeros(0, np.float32); marks = []; n = int(0.06 * SR)
    for label, y in seq:
        if len(demo) == 0: demo = y.copy()
        else:
            w = np.linspace(0, 1, n, dtype=np.float32); demo[-n:] = demo[-n:] * (1 - w) + y[:n] * w; demo = np.concatenate([demo, y[n:]])
        marks.append(f"{label} {max(0, (len(demo) - len(y)) / SR):.0f}s")
    write(f"{ROOT}/demos/{bike}_balanced_demo.wav", demo); print(f"{bike} demo:", " | ".join(marks))

# manifest for the mod: one entry per bike
manifest = {"format": "mono wav 44100 Hz 16-bit, loudness-balanced (see levels.json)", "base_set": "fatboy (not a bike; its ride clips are everyone's cruise base)", "bikes": {}}
for bike, spec in ROSTER.items():
    manifest["bikes"][bike] = {"label": spec["label"], "era": spec["era"], "speed": spec["speed"],
                               "startup": f"{bike}/startup_short.wav", "startup_full": f"{bike}/startup_full.wav", "idle_loop": f"{bike}/idle_loop.wav",
                               "rev_blip": f"{bike}/rev_blips.wav", "accel": f"{bike}/accel.wav", "cruise_loops": [f"{bike}/cruise_high.wav", f"{bike}/cruise_fast.wav"],
                               "shutoff": f"{bike}/shutoff.wav", "crossfade_seconds": {"startup_to_idle": 0.4, "idle_to_cruise": 0.6, "cruise_to_idle": 0.8},
                               "pitch": {"idle": 1.0, "cruise_min": 0.95, "cruise_max": 1.35}}
json.dump(manifest, open(f"{ROOT}/manifest.json", "w"), indent=2)
json.dump(log, open(f"{ROOT}/levels.json", "w"), indent=2)
