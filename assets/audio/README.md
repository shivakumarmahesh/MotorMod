# Engine audio

All game clips are mono, 44.1 kHz, 16-bit WAV, denoised and loudness-balanced (idle -24 LUFS, cruise high -18,
cruise fast -15, start-up and shut-off -22, blips -17, accel -15; peaks capped at -1 dBFS). Mono matters: Unity places
a mono clip in 3D space so friends hear the bike from where it is.

Rebuild everything with `python assets/audio/process_audio.py` (roster at the top of the script). The source recordings are the author's own
and stay out of the repository (`source/`, see CREDITS.md). The Wraith and Draugr Cycles share the Hel Cycle's bundle and so its sounds.

## How the clips are made

- **Fat Boy base set** (`fatboy/`, not a bike): a Softail start-up recording (start, seamless idle, throttle blips) and
  the Fat Boy 114 ride (cruise high, cruise fast, acceleration pull), plus a Freesound shut-off. The bike's cruise,
  accel, blips and shut-off are these clips resampled to its speed (0.8 = slower and lower). Loops stay seamless
  because the whole loop is stretched the same way.
- **Start-up and idle** come from the Shovelhead recording (`source/shovelhead_start_idle.mp3`): the cold start,
  and a seamless loop cut from the steady idle after it (the loop's end is matched to the audio before its start and
  cross-faded).

## Hel Cycle (`hel`)

Idle: shovel start+idle (cleaned), 28.75 to 35.63 s, loop 6.88 s, seam match 0.595 (1.0 = identical). Speed factor for the Fat Boy clips: 0.8.

| Clip | Seconds | LUFS |
|---|---|---|
| startup_short | 3.9 | -22.0 |
| startup_full | 23.25 | -22.0 |
| idle_loop | 6.88 | -24.0 |
| rev_blips | 5.75 | -17.0 |
| accel | 8.75 | -15.9 (peak-limited, 0.9 dB short of target) |
| cruise_high | 13.34 | -18.0 |
| cruise_fast | 8.21 | -17.2 (peak-limited, 2.2 dB short of target) |
| shutoff | 6.88 | -22.0 |
