"""Generic Blender (headless) prep of a motorcycle model for Unity, driven by a JSON config.

  Blender --background --python prep_bike.py -- <config.json>

Outputs into assets/models/<key>/unity/: <key>.fbx, <key>.glb, <key>_layout.json, textures/*.png,
and renders into assets/models/<key>/renders/: front34, side, split_check, calibration (balls: seat, grips, headlight).
"""
import bpy, sys, os, re, math, json, mathutils

cfg_path = sys.argv[sys.argv.index("--") + 1]
cfg = json.load(open(cfg_path))
ROOT = os.path.abspath(os.path.dirname(cfg_path) + "/..")           # assets/models
key = cfg["key"]; src = os.path.abspath(os.path.join(ROOT, "..", "..", cfg["source"])) if not os.path.isabs(cfg["source"]) else cfg["source"]
out_dir = os.path.join(ROOT, key, "unity"); tex_dir = os.path.join(out_dir, "textures"); render_dir = os.path.join(ROOT, key, "renders")
for d in (out_dir, tex_dir, render_dir): os.makedirs(d, exist_ok=True)
TARGET = cfg.get("target_length", 2.8); CAP = cfg.get("decimate_cap", 30000)

bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith(".fbx"): bpy.ops.import_scene.fbx(filepath=src)
else: bpy.ops.import_scene.gltf(filepath=src)
bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o, do_unlink=True)

def meshes(): return [o for o in bpy.data.objects if o.type == "MESH"]
def select(obs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs: o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
def apply_all():
    bpy.context.view_layer.update(); select(meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
def bounds(obs):
    pts = [o.matrix_world @ mathutils.Vector(c) for o in obs for c in o.bound_box]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]
def center(o): a, b = bounds([o]); return [(a[i] + b[i]) / 2 for i in range(3)]
def size(o): a, b = bounds([o]); return [b[i] - a[i] for i in range(3)]
apply_all()

# hide unwanted parts (weapons etc.)
for pat in cfg.get("hide", []):
    for o in list(meshes()):
        if re.search(pat, o.name, re.I): print("hidden:", o.name); bpy.data.objects.remove(o, do_unlink=True)

# centre in x/y first (some models sit far from the origin)
mn0, mx0 = bounds(meshes()); T0 = mathutils.Matrix.Translation((-(mn0[0] + mx0[0]) / 2, -(mn0[1] + mx0[1]) / 2, -mn0[2]))
for o in meshes(): o.matrix_world = T0 @ o.matrix_world
apply_all()
# single-mesh models: split into loose shells so wheels can be found
if len(meshes()) < 3:
    select(meshes()); bpy.ops.object.join(); bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT"); print("split into", len(meshes()), "shells")

# length axis: the larger horizontal extent (Blender z is up after import)
mn, mx = bounds(meshes()); ext = [mx[i] - mn[i] for i in range(3)]
axis = cfg.get("length_axis", "auto")
if axis == "auto": axis = "x" if ext[0] >= ext[1] else "y"
# turn so the length runs along y, then decide which end is the front and make it -y (Unity +z)
if axis == "x":
    M = mathutils.Matrix.Rotation(math.radians(90), 4, "Z")      # +x -> +y
    for o in meshes(): o.matrix_world = M @ o.matrix_world
    apply_all()
def top_profile():
    prof = {}
    for o in meshes():
        mw = o.matrix_world
        for v in o.data.vertices:
            q = mw @ v.co
            if abs(q.x) < 0.09 * max(ext): b = round(q.y, 1); prof[b] = max(prof.get(b, -1e9), q.z)
    return prof
mn, mx = bounds(meshes()); midy = (mn[1] + mx[1]) / 2
front = cfg.get("front", "auto")
if front == "auto":
    prof = top_profile(); fy = max(prof, key=lambda y: prof[y]); front_sign = -1 if fy < midy else 1   # handlebars side
else:
    front_sign = -1 if front in ("-y", "-x") else 1
if front_sign > 0:
    M = mathutils.Matrix.Rotation(math.pi, 4, "Z")
    for o in meshes(): o.matrix_world = M @ o.matrix_world
    apply_all(); print("turned 180 degrees so the front faces -y")
# scale to target length, centre, ground at z=0
mn, mx = bounds(meshes()); scale = TARGET / (mx[1] - mn[1])
M = mathutils.Matrix.Scale(scale, 4)
for o in meshes(): o.matrix_world = M @ o.matrix_world
apply_all(); mn, mx = bounds(meshes())
T = mathutils.Matrix.Translation((-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -mn[2]))
for o in meshes(): o.matrix_world = T @ o.matrix_world
apply_all(); mn, mx = bounds(meshes()); size_m = [round(mx[i] - mn[i], 3) for i in range(3)]
print("bike size m:", size_m, "scale", round(scale, 4))

# decimate heavy parts
for o in meshes():
    n = len(o.data.polygons)
    if n > CAP:
        select([o]); mod = o.modifiers.new("dec", "DECIMATE"); mod.ratio = CAP / n
        bpy.ops.object.modifier_apply(modifier=mod.name); print(f"decimated {o.name}: {n:,} -> {len(o.data.polygons):,}")

# whole-bike budget: scale every sizeable part down together (models with hundreds of subdivided parts)
TOTAL = cfg.get("total_tris_cap")
if TOTAL:
    tot = sum(len(o.data.polygons) for o in meshes())
    if tot > TOTAL:
        ratio = TOTAL / tot
        for o in meshes():
            if len(o.data.polygons) < 300: continue
            select([o]); mod = o.modifiers.new("dec2", "DECIMATE"); mod.ratio = ratio; bpy.ops.object.modifier_apply(modifier=mod.name)
        print(f"total budget: {tot:,} -> {sum(len(o.data.polygons) for o in meshes()):,} faces")

# snapshot of named parts before any split (named seat / grips / headlight lookups use this)
named = {}
for o in meshes():
    a, b = bounds([o]); mw = o.matrix_world
    named[o.name] = dict(bounds=(a, b), verts=[mw @ v.co for v in o.data.vertices])
def named_match(pat): return [v for k, v in named.items() if re.search(pat, k, re.I)]

# wheels
def radii(o, c):
    """(largest radius from the axle, largest sideways distance from the wheel plane x=c[0], smallest radius) over the part's vertices."""
    mw = o.matrix_world; rs = []; xs = []
    for v in o.data.vertices:
        q = mw @ v.co; rs.append(math.hypot(q.y - c[1], q.z - c[2])); xs.append(abs(q.x - c[0]))
    return max(rs), max(xs), min(rs)
def roundish(o):
    s = size(o); m = max(s[1], s[2]); return m > 0 and abs(s[1] - s[2]) / m <= 0.4
wmode = cfg.get("wheels", "auto")
def find_wheels():
    cands = []
    if wmode.startswith("tyre_material:"):
        name = wmode.split(":", 1)[1]; cands = [o for o in meshes() if o.active_material and o.active_material.name == name]
    elif wmode.startswith("named:"):
        f_pat, r_pat = wmode.split(":", 1)[1].split("|")
        cands = [next(o for o in meshes() if re.search(f_pat, o.name, re.I)), next(o for o in meshes() if re.search(r_pat, o.name, re.I))]
    else:
        H = size_m[2]
        for o in meshes():
            s = size(o); a, b = bounds([o])
            if s[1] < 0.18 * size_m[1] or s[2] < 0.15 * H: continue
            if abs(s[1] - s[2]) / max(s[1], s[2]) > 0.22: continue           # round in the side view
            if s[0] > 0.7 * s[1]: continue                                  # thin sideways
            if a[2] > 0.12 * H: continue                                     # touches the ground
            cands.append(o)
        cands = sorted(cands, key=lambda o: -max(size(o)[1], size(o)[2]))
    return cands
cands = find_wheels()
if len(cands) < 2 and not wmode.startswith("named:"):
    print("no wheels among parts; splitting everything into loose shells and retrying")
    select(meshes()); bpy.ops.object.join(); bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT"); print("shells:", len(meshes()))
    cands = find_wheels()
if len(cands) < 2: raise SystemExit(f"wheel detection failed: {[o.name for o in cands]}")
ymin = min(center(o)[1] for o in cands); ymax = max(center(o)[1] for o in cands)
front_t = max([o for o in cands if center(o)[1] < ymin + 0.25], key=lambda o: max(size(o)[1], size(o)[2]))
rear_t = max([o for o in cands if center(o)[1] > ymax - 0.25], key=lambda o: max(size(o)[1], size(o)[2]))
# A part joins a wheel only if it sits inside the tyre cylinder (radius and width) AND is round in the side view or
# small (knobs, nuts, discs); long thin parts that reach the axle (fork legs, swingarms, fenders) stay with the body.
# Concentric ring-like shells (knobby tread, outer tyre) widen the radius first. Per-bike overrides:
#   "wheel_exclude": regex of part names never to join, "wheel_include": {"front": regex, "rear": regex} forced members.
wx_pat = cfg.get("wheel_exclude"); wi_pat = cfg.get("wheel_include", {})
wheels = {}
for wname, tyre, side in (("WheelFront", front_t, "front"), ("WheelRear", rear_t, "rear")):
    c = center(tyre); R0 = max(size(tyre)[1], size(tyre)[2]) / 2
    halfw = max(0.2, size(tyre)[0] / 2 + 0.08)
    others = [o for o in meshes() if o is not tyre and o not in (front_t, rear_t) and not (wx_pat and re.search(wx_pat, o.name, re.I))]
    info = {o: radii(o, c) for o in others}
    # tread shells: small blocks or round rings in the outer band of the wheel (knobby tyres and tread patterns are many loose shells)
    mm = cfg.get("wheel_member_materials")
    if mm: others = [o for o in others if any(s.material and re.search(mm, s.material.name, re.I) for s in o.material_slots)]
    ring_parts = [o for o in others if info[o][1] <= halfw and info[o][2] >= 0.8 * R0 and info[o][0] <= 1.5 * R0
                  and (max(size(o)[1], size(o)[2]) <= 0.3 * R0 or roundish(o))]
    beyond = [o for o in ring_parts if info[o][0] > R0 + 0.01]
    R = 1.15 * R0 if len(beyond) >= 20 else R0                             # a knobby tread (many shells past the tyre part) widens the wheel a little; a few bolts do not
    hw_cfg = cfg.get("wheel_halfw", {})
    if side in hw_cfg: halfw = float(hw_cfg[side])
    xs = sorted(center(o)[0] for o in ring_parts) or [c[0]]
    c = [xs[len(xs) // 2], c[1], c[2]]                                       # wheel plane: median of the tread shells
    info = {o: radii(o, c) for o in others}
    tread_parts = [o for o in ring_parts if info[o][0] <= R + 0.02]          # the shells that really are the tread (the wider band above only sized the wheel)
    inner = max(0.12, size(tyre)[0] / 2 + 0.02)
    if tread_parts:
        reach = sorted(abs(center(o)[0] - c[0]) + size(o)[0] / 2 for o in tread_parts)
        inner = max(inner, reach[min(len(reach) - 1, int(len(reach) * 0.9))])
    if side not in hw_cfg: halfw = max(halfw, inner + 0.04)                   # tyre width from the tread shells themselves (fat rear tyres)
    members = [tyre]; debug = []
    for o in others:
        forced = bool(side in wi_pat and re.search(wi_pat[side], o.name, re.I))
        if cfg.get("wheel_members") and not forced and not re.search(cfg["wheel_members"], o.name, re.I): continue   # only parts of these source objects may spin
        if cfg.get("wheel_member_materials") and not forced and not any(s.material and re.search(cfg["wheel_member_materials"], s.material.name, re.I) for s in o.material_slots): continue   # loose shells keep their material, not their name
        rmax, xmax, rmin = info[o]
        s = size(o); small = max(s[1], s[2]) <= 0.3 * R; rnd = roundish(o)
        tread = rmin >= 0.8 * R0 and rmax - rmin <= 0.045          # tread blocks are shallow; brackets and fork ends that reach the band are deep
        # inside the tyre: big round parts centred on the axle (rim, discs, the tyre body) and small parts that are tread or sit on the axle;
        # anything centred away from the axle (fork clamp, bracket, rail stub) stays with the body so nothing orbits the wheel
        on_axle = (rmin + rmax) / 2 <= 0.12
        oc = center(o); concentric = math.hypot(oc[1] - c[1], oc[2] - c[2]) <= 0.04
        ok = forced or (rmax <= R + 0.02 and xmax <= halfw and ((rnd and not small and concentric) or (small and (tread or on_axle))))
        if rmax <= 1.4 * R0: debug.append(dict(name=o.name, verts=len(o.data.vertices), size=[round(v, 3) for v in s], centre=[round(v, 3) for v in center(o)], rmin=round(rmin, 3), rmax=round(rmax, 3), xmax=round(xmax, 3), small=small, round=rnd, ring=o in ring_parts, member=ok))
        if ok: members.append(o)
    wheels[wname] = dict(center=c, radius=R, members=members, tyre_parts=[tyre] + tread_parts)
    if os.environ.get("WHEEL_DEBUG"):
        json.dump(dict(tyre=tyre.name, R0=round(R0, 3), R=round(R, 3), halfw=round(halfw, 3), centre=[round(v, 3) for v in c], parts=sorted(debug, key=lambda d: -d["verts"])), open(os.path.join(render_dir, f"{key}_{wname}_debug.json"), "w"), indent=1)
        print(f"  debug: {len(debug)} parts near {wname} written")
    print(f"{wname}: {tyre.name} centre {[round(v,3) for v in c]} radius {R:.3f} (tyre part {R0:.3f}, {len(tread_parts)} tread shells) + {len(members)-1} parts; tyre half width {inner:.2f}, cylinder +-{round(halfw,2)}; wheel plane x {c[0]:+.3f}")
used = {m for w in wheels.values() for m in w["members"]}
body_members = [o for o in meshes() if o not in used]

# tyre texels: every polygon of a wheel member whose vertices all sit in the outer part of the wheel (radius >= 0.72 R)
# is rubber. Its UV outline goes to textures/tyre_uv.json per material; make_unity_textures.py paints those texels
# near-black, non-metallic and dull. Materials already named tyre/tire/rubber are handled by name there.
tyre_uv = {}
for w in wheels.values():
    c = w["center"]; R = w["radius"]
    for o in w["tyre_parts"]:                      # the tyre body and the tread shells only, never cables or brackets that sit inside the wheel
        uv = o.data.uv_layers.active
        if uv is None: continue
        mw = o.matrix_world
        for poly in o.data.polygons:
            if any(math.hypot((mw @ o.data.vertices[v].co).y - c[1], (mw @ o.data.vertices[v].co).z - c[2]) < 0.72 * R for v in poly.vertices): continue
            slot = o.material_slots[poly.material_index] if poly.material_index < len(o.material_slots) else None
            mname = slot.material.name if slot and slot.material else ""
            tyre_uv.setdefault(mname, []).append([[round(uv.data[li].uv.x, 5), round(uv.data[li].uv.y, 5)] for li in poly.loop_indices])
# only materials that carry a real share of the tread are tyre materials; stray engine or frame shells in the outer band are not
total = sum(len(v) for v in tyre_uv.values())
tyre_uv = {k: v for k, v in tyre_uv.items() if total and len(v) >= 0.15 * total}
json.dump(tyre_uv, open(os.path.join(tex_dir, "tyre_uv.json"), "w"))
print("tyre polygons per material:", {k: len(v) for k, v in tyre_uv.items()})

# seat, grips, headlight (measured before joining)
smode = cfg.get("seat", "silhouette")
if smode.startswith("named:"):
    parts = named_match(smode.split(":", 1)[1]); a = [min(p["bounds"][0][i] for p in parts) for i in range(3)]; b = [max(p["bounds"][1][i] for p in parts) for i in range(3)]
    seat = [0.0, round(b[2], 3), round(-(a[1] + b[1]) / 2, 3)]
else:
    prof = top_profile(); cands_y = [y for y in prof if 0.25 * size_m[1] / 2 <= y <= 0.95 * size_m[1] / 2]   # rear half (y > 0 is the rear now)
    low = min(prof[y] for y in cands_y); y_min = min(cands_y, key=lambda y: prof[y]); dip = [y_min]
    for step in (0.1, -0.1):
        y = y_min
        while True:
            y = round(y + step, 1)
            if y in prof and y in cands_y and prof[y] <= low + 0.03: dip.append(y)
            else: break
    seat = [0.0, round(low, 3), round(-sum(dip) / len(dip), 3)]
if "seat_override" in cfg: seat = cfg["seat_override"]
gmode = cfg.get("grips", "auto")
if isinstance(gmode, str) and gmode.startswith("named:"):
    pts = [q for p in named_match(gmode.split(":", 1)[1]) for q in p["verts"]]
    def avg2(side):
        near = [p for p in pts if abs(p.x - side.x) < 0.03]
        return [round(sum(p.x for p in near) / len(near), 3), round(sum(p.z for p in near) / len(near), 3), round(-sum(p.y for p in near) / len(near), 3)]
    grips = [avg2(min(pts, key=lambda p: p.x)), avg2(max(pts, key=lambda p: p.x))]
elif gmode == "auto":
    top = mx[2]; pts = [o.matrix_world @ v.co for o in body_members for v in o.data.vertices]; pts = [p for p in pts if p.z > top - cfg.get("grip_band", 0.18)]
    def avg(side):
        near = [p for p in pts if abs(p.x - side.x) < 0.02]
        return [round(sum(p.x for p in near) / len(near), 3), round(sum(p.z for p in near) / len(near), 3), round(-sum(p.y for p in near) / len(near), 3)]
    grips = [avg(min(pts, key=lambda p: p.x)), avg(max(pts, key=lambda p: p.x))]
    # the two ends must mirror each other; the outermost point is the real bar end, the other side may have picked a lever or mirror
    dy, dz = grips[0][1] - grips[1][1], grips[0][2] - grips[1][2]
    if abs(dy) > 0.02 or abs(dz) > 0.02 or abs(abs(grips[0][0]) - abs(grips[1][0])) > 0.02:
        best = grips[0] if abs(grips[0][0]) >= abs(grips[1][0]) else grips[1]
        print(f"grips asymmetric (dy {dy:+.3f} dz {dz:+.3f}); mirroring the outer end {best}")
        grips = [[-abs(best[0]), best[1], best[2]], [abs(best[0]), best[1], best[2]]]
else: grips = gmode
hmode = cfg.get("headlight", "auto")
if isinstance(hmode, str) and hmode.startswith("named:"):
    parts = named_match(hmode.split(":", 1)[1]); a = [min(p["bounds"][0][i] for p in parts) for i in range(3)]; b = [max(p["bounds"][1][i] for p in parts) for i in range(3)]
    headlight = [round((a[0] + b[0]) / 2, 3), round((a[2] + b[2]) / 2, 3), round(-a[1] + 0.03, 3)]
elif hmode == "auto":
    # 1) a part or material that is obviously the lamp; 2) otherwise the most forward centre-line point high on the front
    lamp_pat = re.compile(r"glass|lens|lamp|light|reflektor|headl", re.I)
    lamps = [o for o in body_members if lamp_pat.search(o.name) or any(s.material and lamp_pat.search(s.material.name) for s in o.material_slots)]
    if lamps:
        # among lamp-ish parts take the one furthest forward; use the centre of the vertices that carry the lamp material
        best = None
        for o in lamps:
            mw = o.matrix_world; idx = [i for i, s in enumerate(o.material_slots) if s.material and lamp_pat.search(s.material.name)]
            vs = [mw @ o.data.vertices[v].co for poly in o.data.polygons if (not idx or poly.material_index in idx) for v in poly.vertices]
            if not vs: continue
            c = [sum(q.x for q in vs) / len(vs), sum(q.y for q in vs) / len(vs), sum(q.z for q in vs) / len(vs)]
            if best is None or c[1] < best[1]: best = c
        headlight = [round(best[0], 3), round(best[2], 3), round(-best[1] + 0.03, 3)]
    else:
        H = size_m[2]; L = size_m[1]
        # 2) a small round part near the front at lamp height (a lamp housing or lens)
        round_parts = []
        for k, v in named.items():
            a, b = v["bounds"]; s = [b[i] - a[i] for i in range(3)]; c = [(a[i] + b[i]) / 2 for i in range(3)]
            if not (0.10 <= max(s[1], s[2]) <= 0.40): continue
            if abs(s[1] - s[2]) / max(s[1], s[2]) > 0.35: continue
            if c[1] > -0.2 * L or not (0.5 * H <= c[2] <= 0.9 * H) or abs(c[0]) > 0.2: continue
            round_parts.append(c)
        if round_parts:
            c = min(round_parts, key=lambda c: c[1]); headlight = [round(c[0], 3), round(c[2], 3), round(-c[1] + 0.08, 3)]; print("headlight from a round front part")
        else:
            pts = [o.matrix_world @ v.co for o in body_members for v in o.data.vertices]
            band = [p for p in pts if 0.62 * H <= p.z <= 0.92 * H and abs(p.x) < 0.15]
            fp = min(band, key=lambda p: p.y)
            headlight = [round(fp.x, 3), round(fp.z, 3), round(-fp.y + 0.03, 3)]
else: headlight = hmode
headlight[0] = round((grips[0][0] + grips[1][0]) / 2, 3)               # the lamp sits on the steering head: halfway between the bar ends
print("seat", seat, "grips", grips, "headlight", headlight)

if os.environ.get("PARTS_DUMP"):
    # every body part (Unity axes: x right, y up, z forward), to choose the steering assembly from
    rows = []
    for o in body_members:
        a, b = bounds([o])
        rows.append(dict(name=o.name, mats=sorted({s.material.name for s in o.material_slots if s.material}), verts=len(o.data.vertices),
                         lo=[round(a[0], 3), round(a[2], 3), round(-b[1], 3)], hi=[round(b[0], 3), round(b[2], 3), round(-a[1], 3)]))
    json.dump(dict(front_axle=[round(v, 3) for v in (wheels["WheelFront"]["center"][0], wheels["WheelFront"]["center"][2], -wheels["WheelFront"]["center"][1])],
                   rear_axle=[round(v, 3) for v in (wheels["WheelRear"]["center"][0], wheels["WheelRear"]["center"][2], -wheels["WheelRear"]["center"][1])],
                   grips=grips, headlight=headlight, seat=seat, parts=rows), open(os.path.join(render_dir, f"{key}_parts.json"), "w"), indent=0)
    print("parts dumped:", len(rows))
    if os.environ.get("PARTS_DUMP") == "only": raise SystemExit(0)

# steering assembly: what turns with the bars (fork, bars, grips, mirrors, headlight, front fender) becomes "Steer", with
# the front wheel inside it. The steering axis runs from the front axle up to cfg steer.top (Unity axes). A part belongs to
# it by name or material ("include"), by its middle lying in one of "include_boxes" (Unity axes, x mirrored), or by its
# middle lying in front of the plane through the axis ("margin" metres in front; a large margin switches this off). Parts
# matching "split" that straddle the plane are cut along it (the Hel Cycle's chains run from the fork to the rear).
steer_members = []; steer_axis = None
scfg = cfg.get("steer")
if scfg:
    B = mathutils.Vector(wheels["WheelFront"]["center"]); tu = scfg["top"]; T = mathutils.Vector((tu[0], -tu[2], tu[1]))
    axis_dir = (T - B).normalized(); n = axis_dir.cross(mathutils.Vector((1, 0, 0))).normalized()
    if n.dot(mathutils.Vector((0, -1, 0))) < 0: n = -n                       # in front = towards -y, the bike's front
    margin = scfg.get("margin", 0.0)
    def label(o):
        # the materials its faces really use: shells split from one object keep every slot of it
        used = {o.material_slots[p.material_index].material.name for p in o.data.polygons
                if p.material_index < len(o.material_slots) and o.material_slots[p.material_index].material}
        return o.name + "|" + "|".join(sorted(used))
    def middle(o):
        mw = o.matrix_world; vs = o.data.vertices
        return sum((mw @ v.co for v in vs), mathutils.Vector()) / max(1, len(vs))
    def in_box(c, box):
        u = (abs(c.x), c.z, -c.y); lo, hi = box
        return all(lo[i] <= u[i] <= hi[i] for i in range(3))
    cut_ahead = []
    if scfg.get("split"):
        # a part that straddles the plane (the Hel Cycle's chains run from the fork to the rear) is broken into its loose
        # pieces (whole chain links), and the pieces whose middle lies in front of the plane, or just behind it, turn
        for o in [o for o in body_members if re.search(scfg["split"], label(o), re.I)]:
            select([o]); bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
            pieces = list(bpy.context.selected_objects)
            ahead = [p for p in pieces if (middle(p) - B).dot(n) > -scfg.get("split_margin", 0.0)]
            body_members.extend(p for p in pieces if p is not o)
            cut_ahead.extend(ahead)
            print(f"steer: {o.name} broken into {len(pieces)} loose pieces, {len(ahead)} of them turn")
    for o in body_members:
        lab = label(o)
        if scfg.get("exclude") and re.search(scfg["exclude"], lab, re.I): continue
        c = middle(o)
        if o in cut_ahead or (scfg.get("include") and re.search(scfg["include"], lab, re.I)) or (c - B).dot(n) > margin or any(in_box(c, b) for b in scfg.get("include_boxes", [])):
            steer_members.append(o)
    body_members = [o for o in body_members if o not in steer_members]
    steer_axis = [[round(B.x, 4), round(B.z, 4), round(-B.y, 4)], [round(T.x, 4), round(T.z, 4), round(-T.y, 4)]]
    print(f"steer: {len(steer_members)} parts turn with the bars: {sorted(o.name for o in steer_members)[:60]}")

# join groups, origins on the axles
def join(members, name, origin):
    select(members); bpy.ops.object.join(); ob = bpy.context.view_layer.objects.active; ob.name = name; ob.data.name = name
    bpy.context.scene.cursor.location = origin; bpy.ops.object.origin_set(type="ORIGIN_CURSOR"); return ob
front = join(wheels["WheelFront"]["members"], "WheelFront", wheels["WheelFront"]["center"])
rear = join(wheels["WheelRear"]["members"], "WheelRear", wheels["WheelRear"]["center"])
steer = join(steer_members, "Steer", wheels["WheelFront"]["center"]) if steer_members else None
body = join(body_members, "Body", (0, 0, 0))
for ob in (front, rear, steer):
    if ob is None: continue
    par = steer if (ob is front and steer is not None) else body                   # the front wheel turns with the fork
    ob.parent = par; ob.matrix_parent_inverse = par.matrix_world.inverted()
# a joined part keeps every material slot its pieces had, used or not (loose shells carry all of their model's); Unity
# makes sub-meshes only for the used ones, so the slot list must match them or the materials land one off
for ob in (body, front, rear, steer):
    if ob is None: continue
    select([ob]); n0 = len(ob.material_slots); bpy.ops.object.material_slot_remove_unused()
    if len(ob.material_slots) != n0: print(f"{ob.name}: removed {n0 - len(ob.material_slots)} unused material slots")
mat_order = {ob.name: [s.material.name if s.material else "" for s in ob.material_slots] for ob in (body, front, rear, steer) if ob is not None}

# flat colour overrides also go on the Blender materials, so the renders show the final look
for mname, rgb in cfg.get("flat_colours", {}).items():
    m = bpy.data.materials.get(mname)
    if m and m.use_nodes:
        b = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if b: b.inputs["Base Color"].default_value = (*rgb, 1.0)
        fm = cfg.get("flat_metal_rough", {}).get(mname)
        if b and fm: b.inputs["Metallic"].default_value = fm[0]; b.inputs["Roughness"].default_value = fm[1]
# textures per material (or flat colours for untextured materials)
def image_for(mat, input_name):
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not bsdf or input_name not in bsdf.inputs or not bsdf.inputs[input_name].is_linked: return None
    node = bsdf.inputs[input_name].links[0].from_node
    for _ in range(4):
        if node.type == "TEX_IMAGE": return node.image
        linked = [i for i in node.inputs if i.is_linked]
        if not linked: return None
        node = linked[0].links[0].from_node
    return None
def flat_png(path, rgb):
    img = bpy.data.images.new("flat", 8, 8); px = []
    for _ in range(64): px += [rgb[0], rgb[1], rgb[2], 1.0]
    img.pixels = px; img.filepath_raw = path; img.file_format = "PNG"; img.save(); bpy.data.images.remove(img)
materials = {}; used_names = {n for v in mat_order.values() for n in v}
for mat in bpy.data.materials:
    if mat.name not in used_names or not mat.use_nodes: continue
    entry = {}
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    for k, inp in (("base", "Base Color"), ("normal", "Normal"), ("mr", "Metallic")):
        img = image_for(mat, inp) or (image_for(mat, "Roughness") if k == "mr" else None)
        if img is None: continue
        path = os.path.join(tex_dir, f"{mat.name}_{k}.png"); img.filepath_raw = path; img.file_format = "PNG"
        try: img.save(); entry[k] = os.path.basename(path)
        except Exception as e: print("image save failed", mat.name, k, e)
    if "base" not in entry and bsdf is not None:
        c = list(bsdf.inputs["Base Color"].default_value)[:3]
        over = cfg.get("flat_colours", {}).get(mat.name)
        if over: c = over
        flat_png(os.path.join(tex_dir, f"{mat.name}_base.png"), c); entry["base"] = f"{mat.name}_base.png"
        metal = float(bsdf.inputs["Metallic"].default_value); rough = float(bsdf.inputs["Roughness"].default_value)
        fm = cfg.get("flat_metal_rough", {}).get(mat.name)
        if fm: metal, rough = fm
        flat_png(os.path.join(tex_dir, f"{mat.name}_mr.png"), (0.0, rough, metal)); entry["mr"] = f"{mat.name}_mr.png"
        entry["flat"] = True
    materials[mat.name] = entry
print("materials:", {k: list(v) for k, v in materials.items()})
layout = {"key": key, "prefab_name": cfg["prefab_name"], "size_m": size_m, "scale": round(scale, 4), "seat_unity": seat, "grips_unity": grips, "headlight_unity": headlight,
          "WheelFront": {"center_m": [round(v, 4) for v in wheels["WheelFront"]["center"]], "radius_m": round(wheels["WheelFront"]["radius"], 4)},
          "WheelRear": {"center_m": [round(v, 4) for v in wheels["WheelRear"]["center"]], "radius_m": round(wheels["WheelRear"]["radius"], 4)},
          "material_slots": mat_order, "materials": materials, "palette": cfg.get("palette", True), "source": cfg["source"], "licence": cfg.get("licence", "")}
if steer_axis: layout["steer_axis"] = steer_axis
json.dump(layout, open(os.path.join(out_dir, f"{key}_layout.json"), "w"), indent=1)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, f"{key}.glb"), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, f"{key}.fbx"), use_selection=True, path_mode="STRIP", embed_textures=False, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE")
print("exported glb + fbx; total tris", sum(len(o.data.polygons) for o in meshes()))

# renders
scene = bpy.context.scene; scene.render.resolution_x, scene.render.resolution_y = 1280, 800
world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.37, 0.40, 1)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 4; sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35)); scene.collection.objects.link(sun)
fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA")); fill.data.energy = 400; fill.data.size = 4; fill.location = (-3, -3, 2.5); scene.collection.objects.link(fill)
tgt = bpy.data.objects.new("Target", None); tgt.location = (0, 0, size_m[2] * 0.45); scene.collection.objects.link(tgt)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); cam.data.lens = 50; scene.collection.objects.link(cam); scene.camera = cam
tr = cam.constraints.new("TRACK_TO"); tr.target = tgt; tr.track_axis = "TRACK_NEGATIVE_Z"; tr.up_axis = "UP_Y"
bpy.ops.mesh.primitive_plane_add(size=14, location=(0, 0, 0)); ground = bpy.context.active_object; ground.name = "Ground"
gm = bpy.data.materials.new("GroundMat"); gm.diffuse_color = (0.25, 0.25, 0.25, 1); ground.data.materials.append(gm)
try: scene.render.engine = "BLENDER_EEVEE"
except TypeError: scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.eevee.taa_render_samples = 40
L = size_m[1]; views = {"front34": (1.4 * L, -1.25 * L, 0.55 * L), "side": (1.8 * L, 0.0, 0.33 * L)}
for vname, loc in views.items():
    cam.location = loc; scene.render.filepath = os.path.join(render_dir, f"{key}_{vname}.png"); bpy.ops.render.render(write_still=True)
# calibration: balls at seat (white), grips (grey), headlight (black) over the textured side view, plus split check
def ball(name, u, col, r=0.06):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=(u[0], -u[2], u[1])); b = bpy.context.active_object; b.name = name
    m = bpy.data.materials.new(name + "M"); m.use_nodes = True; m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*col, 1); m.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (*col, 1); m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 2.0
    b.data.materials.append(m); return b
balls = [ball("Seat", seat, (1, 1, 1)), ball("GripL", grips[0], (0.6, 0.6, 0.6)), ball("GripR", grips[1], (0.6, 0.6, 0.6)), ball("Head", headlight, (0.05, 0.05, 0.05), 0.08)]
cam.location = (1.6 * L, -0.9 * L, 0.5 * L); scene.render.filepath = os.path.join(render_dir, f"{key}_calibration.png"); bpy.ops.render.render(write_still=True)
for b in balls: bpy.data.objects.remove(b, do_unlink=True)
scene.render.engine = "BLENDER_WORKBENCH"; scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "OBJECT"
body.color = (0.8, 0.8, 0.8, 1); rear.color = (1, 0.1, 0.1, 1); front.color = (0.1, 0.4, 1, 1)
if steer is not None: steer.color = (0.15, 0.85, 0.2, 1)
cam.location = views["side"]; scene.render.filepath = os.path.join(render_dir, f"{key}_split_check.png"); bpy.ops.render.render(write_still=True)
if steer is not None:
    # the steering split from the front three-quarter and from above, with the axis drawn in
    a, b = [mathutils.Vector((p[0], -p[2], p[1])) for p in steer_axis]; mid = (a + b) / 2; d = b - a
    bpy.ops.mesh.primitive_cylinder_add(radius=0.012, depth=d.length * 1.6, location=mid); ax = bpy.context.active_object
    ax.rotation_mode = "QUATERNION"; ax.rotation_quaternion = d.to_track_quat("Z", "Y"); ax.color = (1, 0.9, 0.1, 1)
    for vname, loc in (("steer_front34", (1.3 * L, -1.1 * L, 0.5 * L)), ("steer_top", (0.01, -0.2 * L, 1.6 * L)), ("steer_side", views["side"])):
        cam.location = loc; scene.render.filepath = os.path.join(render_dir, f"{key}_{vname}.png"); bpy.ops.render.render(write_still=True)
print("renders done")
