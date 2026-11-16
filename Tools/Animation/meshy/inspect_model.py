"""Looks at a Meshy model before it is built for the game: prints its size, triangles and
materials, and renders it textured from the front, side, back, three-quarter and from above.

Run: blender -b --python inspect_model.py -- <model.fbx> <texture.png> <out folder>
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
FBX, TEXTURE, OUT = argv[0], argv[1], argv[2]
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
others = [o for o in bpy.context.scene.objects if o.type not in ('MESH',)]
print('INSPECT objects:', [(o.name, o.type) for o in bpy.context.scene.objects])
tris = 0
lo = Vector((1e9, 1e9, 1e9))
hi = -lo
for o in meshes:
    o.data.calc_loop_triangles()
    tris += len(o.data.loop_triangles)
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        lo = Vector(map(min, lo, w))
        hi = Vector(map(max, hi, w))
    print('INSPECT mesh', o.name, 'verts', len(o.data.vertices), 'materials', [m.name for m in o.data.materials if m])
print('INSPECT triangles', tris, 'bounds', tuple(round(x, 3) for x in lo), tuple(round(x, 3) for x in hi),
      'size', tuple(round(x, 3) for x in (hi - lo)))

# Texture every material with the given picture, unlit, so the render shows the colours as painted.
image = bpy.data.images.load(TEXTURE)
for o in meshes:
    for slot in o.material_slots:
        if slot.material is None:
            continue
        slot.material.use_nodes = True
        nodes = slot.material.node_tree.nodes
        tex = next((n for n in nodes if n.type == 'TEX_IMAGE'), None)
        if tex is None:
            tex = nodes.new('ShaderNodeTexImage')
        tex.image = image

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 700
scene.render.resolution_y = 900
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.75, 0.75, 0.75)

center = (lo + hi) / 2
size = max(hi - lo)
camera_data = bpy.data.cameras.new('cam')
camera_data.type = 'ORTHO'
camera_data.ortho_scale = size * 1.15
camera = bpy.data.objects.new('cam', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera

# Meshy models face -Y in Blender (as the goblin did).
views = {
    'front': (0.0, 0.0), 'side': (90.0, 0.0), 'back': (180.0, 0.0), 'three_quarter': (35.0, 10.0),
    'top': (0.0, 89.0)
}
for name, (yaw, pitch) in views.items():
    distance = size * 3
    yr, pr = math.radians(yaw), math.radians(pitch)
    direction = Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr)))
    camera.location = center + direction * distance
    camera.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(OUT, name + '.png')
    bpy.ops.render.render(write_still=True)
print('INSPECT done')
