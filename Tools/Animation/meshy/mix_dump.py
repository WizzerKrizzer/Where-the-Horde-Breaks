"""Blender: dump a Mixamo FBX clip as world-space bone matrices per frame (plus rest).

blender -b -P mix_dump.py -- clip.fbx out.json
"""
import bpy, sys, json

src, out = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = arm.animation_data.action if arm.animation_data else None
scene = bpy.context.scene
start, end = (int(act.frame_range[0]), int(act.frame_range[1])) if act else (0, 0)
names = [b.name for b in arm.data.bones]
W = arm.matrix_world
rest = {b.name: [list(r) for r in (W @ b.matrix_local)] for b in arm.data.bones}
frames = []
for f in range(start, end + 1):
    scene.frame_set(f)
    frames.append({pb.name: [list(r) for r in (W @ pb.matrix)] for pb in arm.pose.bones})
json.dump(dict(names=names, parents={b.name: (b.parent.name if b.parent else None) for b in arm.data.bones},
               rest=rest, frames=frames, fps=scene.render.fps, start=start, end=end), open(out, "w"))
print("DUMP", src, "bones", len(names), "frames", len(frames), "fps", scene.render.fps, "action", act.name if act else None)
