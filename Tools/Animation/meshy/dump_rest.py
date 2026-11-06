"""Blender: write the Meshy armature's rest skeleton (armature space) to JSON."""
import bpy, sys, json

src, out = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Icosphere"))
bones = []
for b in arm.data.bones:
    bones.append(dict(name=b.name, parent=b.parent.name if b.parent else None,
                      matrix=[list(r) for r in b.matrix_local], head=list(b.head_local), tail=list(b.tail_local),
                      rotation_mode=arm.pose.bones[b.name].rotation_mode))
data = dict(armature_world=[list(r) for r in arm.matrix_world], mesh_world=[list(r) for r in mesh.matrix_world],
            mesh_parent=mesh.parent.name if mesh.parent else None, fps=bpy.context.scene.render.fps,
            bones=bones)
json.dump(data, open(out, "w"), indent=1)
print("DUMPED", len(bones), "fps", data["fps"])
