import bpy, sys, math, mathutils

args = sys.argv[sys.argv.index("--") + 1:]
glb, out = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]

mat = bpy.data.materials.new("vc")
mat.use_nodes = True
nodes = mat.node_tree.nodes
attr = nodes.new("ShaderNodeVertexColor")
bsdf = nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 1.0
mat.node_tree.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])

lo = mathutils.Vector((1e9, 1e9, 1e9))
hi = -lo
tris = 0
for o in meshes:
    o.data.materials.clear()
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = False
    o.data.calc_loop_triangles()
    tris += len(o.data.loop_triangles)
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        lo = mathutils.Vector(map(min, lo, w))
        hi = mathutils.Vector(map(max, hi, w))
print("STATS tris=%d size=%s" % (tris, tuple(round(x, 3) for x in (hi - lo))))

height = hi.z - lo.z
scale = 1.8 / height
center = (lo + hi) / 2
for o in meshes:
    o.matrix_world = mathutils.Matrix.Translation((-center.x * scale, -center.y * scale, -lo.z * scale)) @ mathutils.Matrix.Scale(scale, 4) @ o.matrix_world

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 900, 900
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.5, 0.5, 0.5)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3
sun.rotation_euler = (math.radians(40), 0, math.radians(-150))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 2.3
scene.collection.objects.link(cam)
scene.camera = cam


def shot(name, pitch, yaw, distance=20):
    p, y = math.radians(pitch), math.radians(yaw)
    pos = mathutils.Vector((math.sin(y) * -math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * distance
    target = mathutils.Vector((0, 0, 0.9))
    cam.location = target + pos
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = out + "_" + name + ".png"
    bpy.ops.render.render(write_still=True)


shot("front", 5, 0)
shot("side", 5, 90)
shot("back", 5, 180)
shot("top75", 75, 20)
