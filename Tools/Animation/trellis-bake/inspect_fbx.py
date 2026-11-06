import bpy, sys, math, mathutils

args = sys.argv[sys.argv.index("--") + 1:]
fbx, out = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
tris = verts = 0
lo = mathutils.Vector((1e9, 1e9, 1e9))
hi = -lo
for o in meshes:
    m = o.data
    m.calc_loop_triangles()
    tris += len(m.loop_triangles)
    verts += len(m.vertices)
    for v in m.vertices:
        w = o.matrix_world @ v.co
        lo = mathutils.Vector(map(min, lo, w))
        hi = mathutils.Vector(map(max, hi, w))
print("STATS objects=%d tris=%d verts=%d size=%s materials=%s" % (
    len(meshes), tris, verts, tuple(round(x, 3) for x in (hi - lo)),
    [s.material.name for o in meshes for s in o.material_slots if s.material]))
for img in bpy.data.images:
    print("IMAGE", img.name, tuple(img.size))

# Normalise to about 5 m across (our tower size) standing on z=0, centred.
size = max(hi.x - lo.x, hi.y - lo.y)
scale = 5.0 / size
center = (lo + hi) / 2
for o in meshes:
    o.matrix_world = mathutils.Matrix.Translation((-center.x * scale, -center.y * scale, -lo.z * scale)) @ mathutils.Matrix.Scale(scale, 4) @ o.matrix_world

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.12, 0.08, 0.06)
bpy.ops.mesh.primitive_plane_add(size=60)
ground = bpy.context.object
mat = bpy.data.materials.new("ground")
mat.diffuse_color = (0.13, 0.075, 0.05, 1)
mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.13, 0.075, 0.05, 1)
ground.data.materials.append(mat)

sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 4
sun.rotation_euler = (math.radians(40), 0, math.radians(-125))
scene.collection.objects.link(sun)

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 9
scene.collection.objects.link(cam)
scene.camera = cam

def shot(name, pitch, yaw, distance=40):
    p = math.radians(pitch)
    y = math.radians(yaw)
    # Camera looks down at pitch degrees from horizontal, from the -Y side rotated by yaw.
    pos = mathutils.Vector((math.sin(y) * -math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * distance
    target = mathutils.Vector((0, 0, 1.2))
    cam.location = target + pos
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = out + "/" + name + ".png"
    bpy.ops.render.render(write_still=True)

shot("game-75deg", 75, 0)
shot("game-75deg-back", 75, 180)
shot("side-30deg", 30, 30)
