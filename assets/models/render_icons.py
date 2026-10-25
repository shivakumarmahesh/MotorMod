"""Blender (headless): transparent-background icon renders of the exported bikes.
  Blender --background --python render_icons.py -- <bike.glb> <out.png>
"""
import bpy, sys, math, mathutils
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
pts = [o.matrix_world @ mathutils.Vector(c) for o in meshes for c in o.bound_box]
mn = [min(p[i] for p in pts) for i in range(3)]; mx = [max(p[i] for p in pts) for i in range(3)]
ctr = mathutils.Vector([(mn[i] + mx[i]) / 2 for i in range(3)]); ext = max(mx[i] - mn[i] for i in range(3))
scene = bpy.context.scene; scene.render.resolution_x = scene.render.resolution_y = 512
scene.render.film_transparent = True; scene.render.image_settings.color_mode = "RGBA"
world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True; world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(55), math.radians(15), math.radians(-40)); scene.collection.objects.link(sun)
fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA")); fill.data.energy = 300; fill.data.size = 5; fill.location = (3, 3, 2); scene.collection.objects.link(fill)
tgt = bpy.data.objects.new("T", None); tgt.location = ctr; scene.collection.objects.link(tgt)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); cam.data.type = "ORTHO"; cam.data.ortho_scale = ext * 1.15; scene.collection.objects.link(cam); scene.camera = cam
tr = cam.constraints.new("TRACK_TO"); tr.target = tgt; tr.track_axis = "TRACK_NEGATIVE_Z"; tr.up_axis = "UP_Y"
cam.location = ctr + mathutils.Vector((ext * 1.4, -ext * 1.1, ext * 0.55))      # front three-quarter, slightly above
try: scene.render.engine = "BLENDER_EEVEE"
except TypeError: scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.eevee.taa_render_samples = 32
scene.render.filepath = out; bpy.ops.render.render(write_still=True); print("icon rendered", out)
