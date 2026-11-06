"""Blender: pose the Meshy warrior with retargeted frames and dump posed triangles + colours.

blender -b -P mix_apply.py -- meshy.glb keys.json out.npz step
"""
import bpy, sys, json
import numpy as np
from mathutils import Quaternion

src, keys_path, out_path, step = sys.argv[sys.argv.index("--") + 1:]
step = int(step)
data = json.load(open(keys_path))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
if arm.animation_data:
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
img = next(i for i in bpy.data.images if i.size[0] > 0)
tex = np.array(img.pixels[:]).reshape(img.size[1], img.size[0], 4)[:, :, :3]
uv = None
out = {}
for f in range(0, len(data["frames"]), step):
    for name, (loc, q, sc) in data["frames"][f].items():
        pb = arm.pose.bones[name]
        pb.location = loc
        pb.rotation_quaternion = Quaternion(q)
        pb.scale = sc
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    m = ev.to_mesh()
    m.calc_loop_triangles()
    co = np.array([v.co for v in m.vertices])
    if uv is None:
        uv = np.array([d.uv for d in m.uv_layers.active.data])
        tri_loops = np.array([list(lt.loops) for lt in m.loop_triangles])
        u = uv[tri_loops].mean(1)
        px = (np.clip(u[:, 0] % 1, 0, 0.999) * tex.shape[1]).astype(int)
        py = (np.clip(u[:, 1] % 1, 0, 0.999) * tex.shape[0]).astype(int)
        colors = tex[py, px]
    tris = co[np.array([list(lt.vertices) for lt in m.loop_triangles])]
    ev.to_mesh_clear()
    out[f"t{f}"] = tris.astype(np.float32)
    out[f"c{f}"] = colors.astype(np.float32)
np.savez_compressed(out_path, **out)
print("APPLIED", out_path, len(out) // 2)
