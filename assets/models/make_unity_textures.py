"""Convert every bike's and prop's textures to Unity's Standard-shader layout and apply the mod's colour rule (black, greys and white; brown only for dirt, rust and leather).
Run with the repo venv:  ./.venv/bin/python assets/models/make_unity_textures.py
Reads assets/models/<key>/unity/<key>_layout.json; writes unity/MotorModUnity/Assets/Bikes/<key>/Textures and slots.txt
(props, such as the saddlebag: Assets/Props/<key>/), plus Assets/Bikes/bikes.txt and Assets/Props/props.txt (key:prefab per
line) for the Unity build script.
"""
import glob, json, os, sys, colorsys
from PIL import Image
import numpy as np
ROOT = os.path.abspath(os.path.dirname(__file__) + "/../..")
MODELS = ROOT + "/assets/models"; UNITY = ROOT + "/unity/MotorModUnity/Assets/Bikes"; PROPS = ROOT + "/unity/MotorModUnity/Assets/Props"
MAXTEX = 1024

def palette(alb):
    """Black, greys, white only; brown (hue 15-50 deg, not too saturated or dark-brown leather/rust) is kept; everything else becomes a grey of equal brightness."""
    a = np.asarray(alb.convert("RGBA")).astype(np.float32) / 255.0
    rgb = a[..., :3]; mx = rgb.max(-1); mn = rgb.min(-1); sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hue = np.zeros_like(mx); d = np.maximum(mx - mn, 1e-6)
    hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60.0
    brown = (hue >= 12) & (hue <= 52) & (sat <= 0.75) & (mx <= 0.75)
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    grey = np.stack([lum, lum, lum], -1)
    out = np.where(((sat > 0.12) & ~brown)[..., None], grey, rgb)
    res = np.concatenate([out, a[..., 3:4]], -1)
    return Image.fromarray((np.clip(res, 0, 1) * 255).astype(np.uint8), "RGBA"), float(((sat > 0.12) & ~brown).mean() * 100)

def leather(alb):
    """All of it is leather (the saddlebag): reddish browns move into the brown band and strong saturation is capped;
    nothing turns grey. Returns the image and the share of texels that changed hue."""
    a = np.asarray(alb.convert("RGBA")).astype(np.float32) / 255.0
    rgb = a[..., :3]; mx = rgb.max(-1); mn = rgb.min(-1); d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60.0
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    coloured = sat > 0.12
    h2 = np.where(coloured, np.clip(np.where(hue > 180, hue - 360, hue), 20, 32), hue) / 60.0
    s2 = np.minimum(sat, 0.68); v = mx
    i = np.floor(h2) % 6; f = h2 - np.floor(h2)
    p, q, t = v * (1 - s2), v * (1 - s2 * f), v * (1 - s2 * (1 - f))
    out = np.select([(i == k)[..., None] for k in range(6)], [np.stack(c, -1) for c in ((v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q))])
    res = np.concatenate([np.where(coloured[..., None], out, rgb), a[..., 3:4]], -1)
    changed = coloured & ((hue < 20) | (hue > 32))
    return Image.fromarray((np.clip(res, 0, 1) * 255).astype(np.uint8), "RGBA"), float(changed.mean() * 100)

def ms_from_gltf(path, rubber=False):
    im = Image.open(path).convert("RGBA"); r, g, b, a = im.split()
    metal = b; smooth = g.point(lambda v: 255 - v)
    if rubber: metal = Image.new("L", im.size, 0); smooth = Image.new("L", im.size, 60)
    return Image.merge("RGBA", (metal, metal, metal, smooth))

def tyre_mask(src, mat, size):
    """White where the material's texels belong to a tyre (from prep_bike.py's tyre_uv.json), else black; None when there are none."""
    path = f"{src}/textures/tyre_uv.json"
    if not os.path.exists(path): return None
    uv = json.load(open(path)); total = sum(len(v) for v in uv.values())
    polys = uv.get(mat, [])
    if not polys or len(polys) < 0.15 * total: return None          # only materials that carry a real share of the tread
    from PIL import ImageDraw
    mask = Image.new("L", size, 0); d = ImageDraw.Draw(mask); W, H = size
    for poly in polys:
        pts = [((u % 1.0) * W, (1.0 - (v % 1.0)) * H) for u, v in poly]
        if len(pts) >= 3: d.polygon(pts, fill=255, outline=255)
    return mask

def apply_rubber(alb, ms, mask):
    """Inside the mask: near-black albedo, metallic 0, smoothness about 0.25."""
    if alb is not None:
        dark = Image.eval(alb.convert("L"), lambda v: int(v * 0.18) + 10).convert("RGBA"); dark.putalpha(255)
        alb = Image.composite(dark, alb, mask.resize(alb.size))
    if ms is not None:
        rub = Image.merge("RGBA", (Image.new("L", ms.size, 0),) * 3 + (Image.new("L", ms.size, 64),))
        ms = Image.composite(rub, ms, mask.resize(ms.size))
    return alb, ms

def shrink(im):
    if max(im.size) > MAXTEX: im = im.resize((min(im.width, MAXTEX), min(im.height, MAXTEX)), Image.LANCZOS)
    return im

def convert(key, layout):
    src = f"{MODELS}/{key}/unity"
    folder = f"{PROPS if layout.get('kind') == 'prop' else UNITY}/{key}"
    dst = f"{folder}/Textures"; os.makedirs(dst, exist_ok=True)
    changed = []
    masked = []
    for mat, files in layout["materials"].items():
        rubber = any(w in mat.lower() for w in ("tire", "tyre", "rubber"))
        alb = ms = None
        if "base" in files:
            alb = Image.open(f"{src}/textures/{files['base']}").convert("RGBA")
            if layout.get("palette") == "leather":
                alb, pct = leather(alb)
                if pct > 1: changed.append(f"{mat} {pct:.0f}% (hue into brown)")
            elif layout.get("palette", True):
                alb, pct = palette(alb)
                if pct > 1: changed.append(f"{mat} {pct:.0f}%")
            if rubber: alb = Image.eval(alb, lambda v: int(v * 0.6)); alb.putalpha(255)
        if "mr" in files: ms = ms_from_gltf(f"{src}/textures/{files['mr']}", rubber)
        if not rubber and (alb is not None or ms is not None):
            if files.get("flat"):                                   # a flat 8x8 colour shared with a tyre needs room for the mask
                if alb is not None: alb = alb.resize((512, 512), Image.NEAREST)
                if ms is not None: ms = ms.resize((512, 512), Image.NEAREST)
            mask = tyre_mask(src, mat, (alb or ms).size)
            if mask is None and files.get("flat"):
                if alb is not None: alb = alb.resize((8, 8))
                if ms is not None: ms = ms.resize((8, 8))
            if mask is not None:
                alb, ms = apply_rubber(alb, ms, mask); masked.append(f"{mat} {np.asarray(mask).mean() / 2.55:.0f}%")
        if alb is not None: shrink(alb).save(f"{dst}/{mat}_albedo.png")
        if "normal" in files: shrink(Image.open(f"{src}/textures/{files['normal']}").convert("RGB")).save(f"{dst}/{mat}_normal.png")
        if ms is not None: shrink(ms).save(f"{dst}/{mat}_ms.png")
    if masked: print(f"{key}: tyre texels painted rubber in {', '.join(masked)}")
    with open(f"{folder}/slots.txt", "w") as f:
        for obj, slots in layout["material_slots"].items(): f.write(obj + ":" + ",".join(slots) + "\n")
    print(f"{key}: {len(layout['materials'])} materials; recoloured: {', '.join(changed) if changed else 'nothing'}")
    return layout["prefab_name"]

# always every model: bikes.txt and props.txt list exactly what the Unity build makes
keys = sorted(os.path.basename(os.path.dirname(os.path.dirname(p))) for p in glob.glob(f"{MODELS}/*/unity/*_layout.json"))
layouts = {k: json.load(open(f"{MODELS}/{k}/unity/{k}_layout.json")) for k in keys}
os.makedirs(UNITY, exist_ok=True); os.makedirs(PROPS, exist_ok=True)
lists = {"bikes": [], "props": []}
for k in keys:
    kind = "props" if layouts[k].get("kind") == "prop" else "bikes"
    lists[kind].append(f"{k}:{convert(k, layouts[k])}")
with open(f"{UNITY}/bikes.txt", "w") as f: f.write("".join(l + "\n" for l in lists["bikes"]))
with open(f"{PROPS}/props.txt", "w") as f: f.write("".join(l + "\n" for l in lists["props"]))
print("bikes:", lists["bikes"], "props:", lists["props"])
