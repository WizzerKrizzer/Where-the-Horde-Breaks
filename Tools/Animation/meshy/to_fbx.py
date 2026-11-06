import bpy, sys, os
src, dst, tex_out = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, [m.name for m in o.modifiers] if o.type == "MESH" else "")
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o)
print("ACTIONS", [(a.name, a.frame_range[:]) for a in bpy.data.actions])
for img in bpy.data.images:
    if img.size[0] > 0:
        img.filepath_raw = tex_out
        img.file_format = "PNG"
        img.save()
        print("TEX", img.name, img.size[:])
        break
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0, apply_scale_options="FBX_SCALE_UNITS", path_mode="STRIP", embed_textures=False)
print("WROTE", dst, os.path.getsize(dst))
