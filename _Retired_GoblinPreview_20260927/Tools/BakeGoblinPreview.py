"""Bake the supplied Meshy GLB into a small Unity GPU vertex-animation preview.

Requires numpy. Does not modify the source GLB. Supports this asset's LINEAR/STEP
TRS animation, four skin influences, and embedded base colour texture.
"""
import argparse
import json
import pathlib
import struct
import numpy as np


def load_glb(source):
    data = source.read_bytes()
    size = struct.unpack_from('<I', data, 12)[0]
    gltf = json.loads(data[20:20 + size])
    binary = data[28 + size:]
    types = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}
    widths = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}

    def accessor(index):
        a = gltf['accessors'][index]
        if 'sparse' in a:
            raise ValueError('Sparse accessor not supported')
        v = gltf['bufferViews'][a['bufferView']]
        dtype = np.dtype(types[a['componentType']])
        n = widths[a['type']]
        result = np.ndarray((a['count'], n), dtype=dtype, buffer=binary,
                            offset=v.get('byteOffset', 0) + a.get('byteOffset', 0),
                            strides=(v.get('byteStride', dtype.itemsize * n), dtype.itemsize)).copy()
        if a.get('normalized'):
            result = result.astype(float) / np.iinfo(dtype).max
        return result

    return gltf, binary, accessor


def quaternion_multiply(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([
        aw*bx + ax*bw + ay*bz - az*by,
        aw*by - ax*bz + ay*bw + az*bx,
        aw*bz + ax*by - ay*bx + az*bw,
        aw*bw - ax*bx - ay*by - az*bz])


def quaternion_inverse(q):
    q = np.asarray(q, dtype=float)
    return np.array([-q[0], -q[1], -q[2], q[3]]) / np.dot(q, q)


def bake(source, output, animation_source=None, clip_name='Running', animation_name=None):
    gltf, binary, accessor = load_glb(source)
    animation_source = animation_source or source
    animation_gltf, _, animation_accessor = load_glb(animation_source)

    def trs(t, q, s):
        x, y, z, w = q / np.linalg.norm(q)
        m = np.eye(4)
        m[:3, :3] = np.array([
            [1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
            [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
            [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]) @ np.diag(s)
        m[:3, 3] = t
        return m

    nodes = gltf['nodes']
    parents = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}
    mesh_node = next(n for n in nodes if 'mesh' in n and 'skin' in n)
    primitives = gltf['meshes'][mesh_node['mesh']]['primitives']
    if len(primitives) != 1:
        raise ValueError('Preview expects one primitive')
    primitive = primitives[0]
    attr = primitive['attributes']
    pos = accessor(attr['POSITION'])
    norm = accessor(attr['NORMAL'])
    uv = accessor(attr['TEXCOORD_0']).astype('<f4')
    uv[:, 1] = 1 - uv[:, 1]  # glTF upper-left texture origin -> Unity lower-left.
    joints = accessor(attr['JOINTS_0']).astype(int)
    weights = accessor(attr['WEIGHTS_0'])
    weights /= weights.sum(axis=1, keepdims=True)
    indices = accessor(primitive['indices']).reshape(-1, 3).astype('<i4')
    # Reflect X to Unity's handedness: preserves +Z as the character's forward.
    indices = indices[:, [0, 2, 1]].copy()
    skin = gltf['skins'][mesh_node['skin']]
    inverse_bind = accessor(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    animations = animation_gltf.get('animations', [])
    if not animations:
        raise ValueError(f'No animations found in {animation_source}')
    if animation_name is None:
        animation = animations[0]
    else:
        animation = next((candidate for candidate in animations
                          if candidate.get('name') == animation_name), None)
        if animation is None:
            available = ', '.join(candidate.get('name', '<unnamed>') for candidate in animations)
            raise ValueError(f'Animation {animation_name!r} not found. Available: {available}')
    animation_nodes = animation_gltf['nodes']
    target_nodes_by_name = {node.get('name'): i for i, node in enumerate(nodes) if node.get('name')}
    retarget = animation_source.resolve() != source.resolve()
    source_height = float(np.ptp(accessor(attr['POSITION'])[:, 1]))
    animation_mesh_node = next(n for n in animation_nodes if 'mesh' in n and 'skin' in n)
    animation_primitive = animation_gltf['meshes'][animation_mesh_node['mesh']]['primitives'][0]
    animation_positions = animation_accessor(animation_primitive['attributes']['POSITION'])
    animation_height = max(1e-6, float(np.ptp(animation_positions[:, 1])))
    translation_scale = source_height / animation_height
    channels = []
    for c in animation['channels']:
        s = animation['samplers'][c['sampler']]
        if s.get('interpolation', 'LINEAR') not in ('LINEAR', 'STEP'):
            raise ValueError('Unsupported interpolation')
        source_target = c['target']
        source_node = animation_nodes[source_target['node']]
        node_name = source_node.get('name')
        if node_name not in target_nodes_by_name:
            raise ValueError(f'Animation bone {node_name!r} does not exist in target model')
        target_index = target_nodes_by_name[node_name]
        path = source_target['path']
        values = animation_accessor(s['output']).astype(float)
        if retarget:
            target_node = nodes[target_index]
            if path == 'rotation':
                source_rest = np.array(source_node.get('rotation', [0, 0, 0, 1]), dtype=float)
                target_rest = np.array(target_node.get('rotation', [0, 0, 0, 1]), dtype=float)
                rest_delta = quaternion_multiply(target_rest, quaternion_inverse(source_rest))
                values = np.array([quaternion_multiply(rest_delta, value) for value in values])
                values /= np.maximum(np.linalg.norm(values, axis=1, keepdims=True), 1e-8)
            elif path == 'translation':
                source_rest = np.array(source_node.get('translation', [0, 0, 0]), dtype=float)
                target_rest = np.array(target_node.get('translation', [0, 0, 0]), dtype=float)
                values = target_rest + (values - source_rest) * translation_scale
            elif path == 'scale':
                source_rest = np.array(source_node.get('scale', [1, 1, 1]), dtype=float)
                target_rest = np.array(target_node.get('scale', [1, 1, 1]), dtype=float)
                values = target_rest * values / np.maximum(np.abs(source_rest), 1e-8)
        channels.append(({'node': target_index, 'path': path}, animation_accessor(s['input']).ravel(),
                         values, s.get('interpolation', 'LINEAR')))
    duration = max(float(c[1][-1]) for c in channels)
    frame_count = max(2, round(duration * 30))
    positions, normals = [], []
    homogeneous = np.column_stack((pos, np.ones(len(pos))))
    for time in np.linspace(0, duration, frame_count, endpoint=False):
        transforms = [{k: np.array(n.get(k, default), dtype=float) for k, default in
                       [('translation', [0, 0, 0]), ('rotation', [0, 0, 0, 1]), ('scale', [1, 1, 1])]} for n in nodes]
        for target, times, values, interp in channels:
            i = int(np.clip(np.searchsorted(times, time, side='right') - 1, 0, len(times)-1))
            j = min(i+1, len(times)-1)
            alpha = 0 if j == i or interp == 'STEP' else (time-times[i]) / (times[j]-times[i])
            a, b = values[i].astype(float), values[j].astype(float)
            if target['path'] == 'rotation':
                if np.dot(a, b) < 0:
                    b = -b
                dot = np.clip(np.dot(a, b), -1, 1)
                if dot < 0.9995:
                    angle = np.arccos(dot)
                    value = (np.sin((1-alpha)*angle)*a + np.sin(alpha*angle)*b) / np.sin(angle)
                else:
                    value = a*(1-alpha) + b*alpha
                value /= np.linalg.norm(value)
            else:
                value = a*(1-alpha) + b*alpha
            transforms[target['node']][target['path']] = value
        world = {}
        def global_matrix(i):
            if i not in world:
                t = transforms[i]
                local = np.array(nodes[i]['matrix']).reshape(4, 4).T if 'matrix' in nodes[i] else trs(t['translation'], t['rotation'], t['scale'])
                world[i] = global_matrix(parents[i]) @ local if i in parents else local
            return world[i]
        matrices = np.array([global_matrix(i) for i in skin['joints']]) @ inverse_bind
        blended = (matrices[joints] * weights[:, :, None, None]).sum(axis=1)
        p = np.einsum('nij,nj->ni', blended, homogeneous)[:, :3]
        n = np.einsum('nij,nj->ni', blended[:, :3, :3], norm)
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-8)
        p[:, 0] *= -1
        n[:, 0] *= -1
        positions.append(p)
        normals.append(n)
    positions = np.array(positions)
    normals = np.array(normals)
    # Constant normalization preserves vertical running bounce. Body height = 2.4
    # local units, multiplied by the existing enemy scale (~0.34) in the shader.
    low, high = positions.min(axis=(0, 1)), positions.max(axis=(0, 1))
    scale = 2.4 / (high[1] - low[1])
    origin = np.array([(low[0]+high[0])/2, low[1], (low[2]+high[2])/2])
    positions = (positions - origin) * scale
    assert np.isfinite(positions).all() and np.isfinite(normals).all()
    width = 256
    height = (frame_count * len(pos) + width - 1) // width
    def atlas(values):
        pixels = np.zeros((width * height, 4), dtype='<f2')
        pixels[:frame_count * len(pos), :3] = values.reshape(-1, 3)
        return pixels.tobytes()
    output.mkdir(parents=True, exist_ok=True)
    with (output / f'{clip_name}.bytes').open('wb') as f:
        f.write(struct.pack('<6If', 0x474F4231, len(pos), indices.size, frame_count, width, height, duration))
        for array in (positions[0].astype('<f4'), normals[0].astype('<f4'), uv, indices):
            f.write(array.tobytes())
        f.write(atlas(positions))
        f.write(atlas(normals))
    material = gltf['materials'][primitive['material']]['pbrMetallicRoughness']
    texture = gltf['textures'][material['baseColorTexture']['index']]
    image = gltf['images'][texture['source']]
    view = gltf['bufferViews'][image['bufferView']]
    offset = view.get('byteOffset', 0)
    (output / 'BaseColor.bytes').write_bytes(binary[offset:offset+view['byteLength']])
    report = dict(source=str(source), animation_source=str(animation_source), retargeted=retarget,
                  animation=animation.get('name'), clip=clip_name, vertices=len(pos),
                  triangles=indices.size//3, frames=frame_count, duration=duration,
                  atlas=[width, height], bounds=[positions.min(axis=(0,1)).tolist(), positions.max(axis=(0,1)).tolist()])
    (output / f'{clip_name}.report.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=pathlib.Path)
    parser.add_argument('output', type=pathlib.Path)
    parser.add_argument('--animation-source', type=pathlib.Path)
    parser.add_argument('--clip-name', default='Running')
    parser.add_argument('--animation-name', help='Exact animation name inside a merged GLB')
    args = parser.parse_args()
    bake(args.source, args.output, args.animation_source, args.clip_name, args.animation_name)
