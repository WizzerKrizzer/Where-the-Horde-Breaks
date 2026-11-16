"""Renders every few frames of an animated FBX from the side and from the front, to choose which
part of the animation to use.

Run: blender -b --python preview_animated.py -- <animated.fbx> <out folder> [step] [action name]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[1]
STEP = int(argv[2]) if len(argv) > 2 else 6
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
scene = bpy.context.scene
action = bpy.data.actions[0]
if len(argv) > 3:
    action = next(a for a in bpy.data.actions if '|' + argv[3] + '|' in a.name)
    rig = next(o for o in scene.objects if o.type == 'ARMATURE')
    rig.animation_data.action = action
    if hasattr(rig.animation_data, 'action_slot') and getattr(action, 'slots', None):
        rig.animation_data.action_slot = action.slots[0]
first, last = (int(x) for x in action.frame_range)
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'SINGLE'
scene.display.shading.single_color = (0.45, 0.6, 0.35)
scene.render.resolution_x = 300
scene.render.resolution_y = 300
scene.render.film_transparent = False
world = bpy.data.worlds.new('W')
scene.world = world
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 4.6
scene.collection.objects.link(cam)
scene.camera = cam
os.makedirs(OUT, exist_ok=True)
armature = next(o for o in scene.objects if o.type == 'ARMATURE')
for frame in range(first, last + 1, STEP):
    scene.frame_set(frame)
    hips = armature.matrix_world @ armature.pose.bones['mixamorig:Hips'].head
    print('FRAME', frame, 'hips', tuple(round(x, 3) for x in hips))
    for view, yaw in (('side', 90), ('front', 0)):
        a = math.radians(yaw)
        centre = Vector((0, 0, 1.1))
        cam.location = centre + Vector((math.sin(a), -math.cos(a), 0.1)) * 8
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(OUT, '%s_%03d.png' % (view, frame))
        bpy.ops.render.render(write_still=True)
