#!/usr/bin/env python3
"""Ask the running harness for pictures and build one contact sheet per bike.

    ./.venv/bin/python tools/harness/shoot.py [bike ...] [--views right,left,front34,rear34] [--markers]
                                               [--time 0.5] [--env Clear] [--timeout 300] [--out DIR]

Writes harness/request.json inside the game's plugin folder, waits for done.json, copies the PNGs into
--out (default: /tmp scratch under harness_shots) and writes <bike>_sheet.png there (2 x 2, labelled).
"""
import argparse, json, os, shutil, sys, time
from pathlib import Path

V = Path.home() / "Library/Application Support/Steam/steamapps/common/Valheim"
P = V / "BepInEx/plugins/MotorMod/harness"
VIEWS = ["right", "left", "front34", "rear34", "top", "front"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("bikes", nargs="*")
    ap.add_argument("--views", default=",".join(VIEWS))
    ap.add_argument("--markers", action="store_true")
    ap.add_argument("--time", type=float, default=0.5, help="time of day 0..1, 0.5 = noon; -1 leaves the clock")
    ap.add_argument("--env", default="Clear", help="force an environment (Clear, Rain, Misty, ...); '' leaves the weather alone")
    ap.add_argument("--grass", action="store_true", help="keep the grass (default: hidden so feet show)")
    ap.add_argument("--settle", type=float, default=2.0)
    ap.add_argument("--fov", type=float, default=40.0)
    ap.add_argument("--timeout", type=int, default=300)
    ap.add_argument("--out", default="")
    ap.add_argument("--tag", default="", help="suffix added to copied picture names")
    ap.add_argument("--probe", action="store_true", help="also write <bike>_profile.txt (mesh top surface grid) next to the pictures")
    ap.add_argument("--ride", type=float, default=0.0, help="seconds of full throttle before the pictures; hand gap measured on consecutive frames")
    ap.add_argument("--fps", type=int, default=0, help="frame-rate cap during the run (0 = 60)")
    ap.add_argument("--water", action="store_true", help="spawn on open water instead of the land stage")
    ap.add_argument("--music", action="store_true", help="run the soundtrack test sequence after mounting (use with --water)")
    ap.add_argument("--fx", default="", help="comma-separated game effect prefabs to photograph at the stage (use bike name none to skip bikes)")
    ap.add_argument("--prefabs", default="", help="regex: list matching spawnable prefabs into prefabs.txt")
    ap.add_argument("--inspect", default="", help="comma-separated prefabs: log their WearNTear objects and effects and their materials' shaders (use bike name none)")
    ap.add_argument("--icons", default="", help="yaw,pitch,fill,flames: re-render the flaming skull icons with these settings into icon_*.png (bike name none)")
    ap.add_argument("--ram", default="", help="creature: one standing right in front with the throttle held, then a full-speed ram (land)")
    ap.add_argument("--wall", default="", help="building piece across the lane, hit at full speed (land); with --boost the crash breaks the bike and is photographed")
    ap.add_argument("--sink", action="store_true", help="on water: plane under boost, settle without it, lift back, wheelie, then brake to a stop: it swamps and breaks apart, materials go down (use with --water)")
    ap.add_argument("--songs", action="store_true", help="decode every song in the game's music folder and compare with the index (use bike name none)")
    ap.add_argument("--boost", action="store_true", help="hold Shift during the ride")
    ap.add_argument("--refund", default="", help="bike key: smashed and hammered bikes drop their recipe loose on the ground, no bag; the hammer refuses a ridden bike")
    ap.add_argument("--fuel", default="", help="bike key: empty when built, Shift+E cores from bag and saddlebags, cores from the hotbar, coal refused, nothing while rolling, runs dry, boost burns more (land)")
    ap.add_argument("--storage", default="", help="bike key: two-slot saddlebags found by the player's look, open with E, shut while ridden, spilled when broken")
    ap.add_argument("--condition", default="", help="bike key: the damage look at 100, 60 and 20 % health and after repair, with pictures (soot, smoke burst, engine smoke)")
    ap.add_argument("--ui", default="", help="bike key: screenshots of the game window: hover text, hammer condition bar and repair, fuel gauge while riding, materials on the ground once hammered")
    ap.add_argument("--seadry", action="store_true", help="on water: run dry under way: the bike slows, swamps and breaks apart, materials go down (use with --water)")
    ap.add_argument("--steer", type=float, default=None, help="hold the steering at this value (-1 full left .. 1 full right) before the pictures")
    ap.add_argument("--gear", default="", help="comma-separated item prefabs the rider wears: armour, cape, a weapon (sheathed on the back or hip)")
    ap.add_argument("--biome", default="", help="stage in this biome instead of the usual one: Plains, AshLands, Mistlands, Mountain, BlackForest, Swamp, DeepNorth")
    ap.add_argument("--skull", default="", help="helmet prefab the rider wears for the pictures, e.g. HelmetHelSkull (views riderhead, riderheadside, riderheadback)")
    ap.add_argument("--census", action="store_true", help="log every live particle system near the bike at the first moving picture")
    ap.add_argument("--wheelie", action="store_true", help="after the ride: hold the wheelie past the top, then feather it (use with --ride)")
    ap.add_argument("--targets", default="", help="comma-separated creatures placed beside the ride path, AI off (use with --ride, land only)")
    a = ap.parse_args()
    if not (P / "ready.json").exists():
        sys.exit(f"harness is not ready ({P/'ready.json'} missing); run tools/mac/harness.sh start")
    ready = json.loads((P / "ready.json").read_text())
    if not ready.get("ok"):
        sys.exit(f"harness refused: {ready}")
    views = [v for v in a.views.split(",") if v]
    req = {"bikes": a.bikes, "views": views, "markers": a.markers, "time": a.time, "env": a.env,
           "width": 1280, "height": 800, "fov": a.fov, "settle": a.settle, "grass": a.grass, "probe": a.probe, "ride": a.ride, "fps": a.fps, "water": a.water, "music": a.music, "fx": [f for f in a.fx.split(",") if f], "prefabs": a.prefabs, "inspect": [x for x in a.inspect.split(",") if x], "icons": a.icons, "wheelie": a.wheelie, "census": a.census, "ram": a.ram, "wall": a.wall, "sink": a.sink, "songs": a.songs, "refund": a.refund, "fuel": a.fuel, "storage": a.storage, "ui": a.ui, "condition": a.condition, "seadry": a.seadry, "steerOn": a.steer is not None, "steer": a.steer or 0.0, "skull": a.skull, "gear": [x for x in a.gear.split(",") if x], "biome": a.biome, "boost": a.boost, "targets": [x for x in a.targets.split(",") if x], "quit": False}
    done = P / "done.json"
    if done.exists():
        done.unlink()
    (P / "request.json").write_text(json.dumps(req))
    t0 = time.time()
    while not done.exists():
        if time.time() - t0 > a.timeout:
            status = (P / "status.json").read_text().strip() if (P / "status.json").exists() else "no status.json"
            sys.exit(f"timed out waiting for done.json; harness status: {status}")
        time.sleep(1)
    time.sleep(0.5)
    result = json.loads(done.read_text())
    print(json.dumps(result, indent=1))
    out = Path(a.out) if a.out else Path(os.environ.get("HARNESS_OUT", "/tmp/harness_shots"))
    out.mkdir(parents=True, exist_ok=True)
    pics = [Path(p) for p in result.get("pictures", [])]
    for prof in list(P.glob("*_profile.txt")) + list(P.glob("prefabs.txt")) + list(P.glob("fx__background.png")):
        shutil.copy2(prof, out / prof.name)
    for p in pics:
        if p.exists():
            shutil.copy2(p, out / (p.stem + (("_" + a.tag) if a.tag else "") + p.suffix))
    sheets = make_sheets(pics, views, out)
    for s in sheets:
        print("sheet:", s)
    return 0 if result.get("ok") else 2


def make_sheets(pics, views, out):
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("Pillow missing; no contact sheets")
        return []
    by_bike = {}
    for p in pics:
        stem = p.stem
        for v in views:
            if stem.endswith("_" + v):
                by_bike.setdefault(stem[: -len(v) - 1], {})[v] = p
    sheets = []
    for bike, vs in by_bike.items():
        imgs = [(v, Image.open(vs[v])) for v in views if v in vs]
        if not imgs:
            continue
        w, h = imgs[0][1].size
        scale = 0.5
        tw, th = int(w * scale), int(h * scale)
        cols = 2 if len(imgs) <= 4 else 3
        rows = (len(imgs) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * tw, rows * (th + 24)), (20, 20, 20))
        d = ImageDraw.Draw(sheet)
        for i, (v, im) in enumerate(imgs):
            x, y = (i % cols) * tw, (i // cols) * (th + 24)
            sheet.paste(im.resize((tw, th)), (x, y + 24))
            d.text((x + 6, y + 6), f"{bike} / {v}", fill=(230, 230, 230))
        path = out / f"{bike}_sheet.png"
        sheet.save(path)
        sheets.append(str(path))
    return sheets


if __name__ == "__main__":
    sys.exit(main())
