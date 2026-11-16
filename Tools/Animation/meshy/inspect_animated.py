"""Lists what an animated FBX holds: its meshes, its bones and its animations (name, frames, rate).

Run: blender -b --python inspect_animated.py -- <animated.fbx>
"""
import sys

import bpy

argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
scene = bpy.context.scene
print('FPS', scene.render.fps, 'scene frames', scene.frame_start, scene.frame_end)
for o in scene.objects:
    print('OBJECT', o.type, o.name, 'parent', o.parent.name if o.parent else None,
          'scale', tuple(round(x, 4) for x in o.scale), 'rot', tuple(round(x, 3) for x in o.rotation_euler))
    if o.type == 'MESH':
        co = [o.matrix_world @ v.co for v in o.data.vertices]
        print('  verts', len(co), 'tris', sum(len(p.vertices) - 2 for p in o.data.polygons),
              'groups', len(o.vertex_groups),
              'min', tuple(round(min(c[i] for c in co), 3) for i in range(3)),
              'max', tuple(round(max(c[i] for c in co), 3) for i in range(3)))
    if o.type == 'ARMATURE':
        print('  bones', len(o.data.bones))
        for b in o.data.bones:
            print('   BONE', b.name, 'parent', b.parent.name if b.parent else None,
                  'head', tuple(round(x, 3) for x in (o.matrix_world @ b.head_local)))
        print('  action', o.animation_data.action.name if o.animation_data and o.animation_data.action else None)
for a in bpy.data.actions:
    print('ACTION', a.name, 'range', tuple(a.frame_range))
