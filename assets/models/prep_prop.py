"""Blender (headless) prep of a small prop (the saddlebag) for Unity, driven by a JSON config.

  Blender --background --python prep_prop.py -- <config.json>

The prop is joined into one mesh and turned so that its width runs along y (Unity z, the bike's length), its height
along z (Unity y) and its front (the side named by "front" in the config) faces +x (Unity +x, away from the bike).
The origin sits on the middle of its back face, where it touches the bike. Outputs into assets/models/<key>/unity/:
<key>.fbx, <key>.glb, <key>_layout.json, textures/*.png, and renders into assets/models/<key>/renders/.
"""
import bpy, sys, os, math, json, mathutils

cfg_path = sys.argv[sys.argv.index("--") + 1]
cfg = json.load(open(cfg_path))
ROOT = os.path.abspath(os.path.dirname(cfg_path) + "/..")           # assets/models
key = cfg["key"]; src = os.path.abspath(os.path.join(ROOT, "..", "..", cfg["source"]))
out_dir = os.path.join(ROOT, key, "unity"); tex_dir = os.path.join(out_dir, "textures"); render_dir = os.path.join(ROOT, key, "renders")
for d in (out_dir, tex_dir, render_dir): os.makedirs(d, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o, do_unlink=True)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join(); prop = bpy.context.view_layer.objects.active; prop.name = prop.data.name = cfg.get("object_name", "Prop")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def bounds():
    pts = [prop.matrix_world @ v.co for v in prop.data.vertices]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]

# turn: the config names the import axis that is the prop's width, and the one its front faces
axes = {"x": mathutils.Vector((1, 0, 0)), "y": mathutils.Vector((0, 1, 0)), "z": mathutils.Vector((0, 0, 1))}
def vec(s): return -axes[s[1]] if s.startswith("-") else axes[s.lstrip("+")]
width, front, up = vec(cfg["width_axis"]), vec(cfg["front"]), vec(cfg["up"])
basis = mathutils.Matrix((front, width, up)).transposed()             # columns: where +x, +y, +z of the result come from
M = basis.inverted().to_4x4()
prop.matrix_world = M @ prop.matrix_world
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mn, mx = bounds(); w = mx[1] - mn[1]
s = cfg.get("width_m", 0.38) / w
prop.matrix_world = mathutils.Matrix.Scale(s, 4) @ prop.matrix_world
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mn, mx = bounds()
# origin: middle of the back face (x = min), centre of width and height
prop.matrix_world = mathutils.Matrix.Translation((-mn[0], -(mn[1] + mx[1]) / 2, -(mn[2] + mx[2]) / 2)) @ prop.matrix_world
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mn, mx = bounds(); size_m = [round(mx[i] - mn[i], 3) for i in range(3)]
print(f"{key}: size (depth, width, height) {size_m} m, scale {s:.4f}, faces {len(prop.data.polygons)}")

# textures
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
materials = {}; slots = []
for i, slot in enumerate(prop.material_slots):
    mat = slot.material
    if mat is None: continue
    name = cfg.get("material_names", {}).get(mat.name, mat.name); mat.name = name; slots.append(name)
    entry = {}
    for k, inp in (("base", "Base Color"), ("normal", "Normal"), ("mr", "Metallic")):
        img = image_for(mat, inp) or (image_for(mat, "Roughness") if k == "mr" else None)
        if img is None: continue
        path = os.path.join(tex_dir, f"{name}_{k}.png"); img.filepath_raw = path; img.file_format = "PNG"
        img.save(); entry[k] = os.path.basename(path)
    materials[name] = entry
print("materials:", materials)
layout = {"key": key, "kind": "prop", "prefab_name": cfg["prefab_name"], "size_m": size_m, "scale": round(s, 5), "material_slots": {"Body": slots},
          "materials": materials, "palette": cfg.get("palette", True), "source": cfg["source"], "licence": cfg.get("licence", "")}
json.dump(layout, open(os.path.join(out_dir, f"{key}_layout.json"), "w"), indent=1)
prop.name = prop.data.name = "Body"
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, f"{key}.glb"), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
# Blender +x (front) -> Unity +x, Blender +y (width) -> Unity +z, Blender +z (up) -> Unity +y
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, f"{key}.fbx"), use_selection=True, path_mode="STRIP", embed_textures=False, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE")

# renders: four sides, so the orientation can be checked by eye
scene = bpy.context.scene; scene.render.resolution_x, scene.render.resolution_y = 640, 480
world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.37, 0.40, 1)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 4; sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35)); scene.collection.objects.link(sun)
tgt = bpy.data.objects.new("Target", None); tgt.location = (size_m[0] / 2, 0, 0); scene.collection.objects.link(tgt)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); cam.data.lens = 50; scene.collection.objects.link(cam); scene.camera = cam
tr = cam.constraints.new("TRACK_TO"); tr.target = tgt; tr.track_axis = "TRACK_NEGATIVE_Z"; tr.up_axis = "UP_Y"
try: scene.render.engine = "BLENDER_EEVEE"
except TypeError: scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.eevee.taa_render_samples = 32
D = 1.4 * max(size_m)
for vname, loc in {"front": (D, 0.0, 0.2 * D), "back": (-D, 0.0, 0.2 * D), "end": (0.3 * D, D, 0.2 * D), "front34": (0.8 * D, -0.7 * D, 0.45 * D)}.items():
    cam.location = loc; scene.render.filepath = os.path.join(render_dir, f"{key}_{vname}.png"); bpy.ops.render.render(write_still=True)
print("renders done")
