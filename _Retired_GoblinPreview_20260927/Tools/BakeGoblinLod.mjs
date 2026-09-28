// node Tools/BakeGoblinLod.mjs <meshopt_simplifier.mjs> <Red resource directory>
// Uses meshoptimizer 1.2.0. Retains original vertex IDs for all six VAT clips.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const { MeshoptSimplifier: simplifier } = await import(pathToFileURL(path.resolve(process.argv[2])));
await simplifier.ready;
const directory = process.argv[3];
const data = fs.readFileSync(path.join(directory, 'Running.bytes'));
const count = data.readUInt32LE(4), indexCount = data.readUInt32LE(8);
const positions = new Float32Array(count * 3);
const attributes = new Float32Array(count * 5);
for (let i = 0; i < count; i++) {
    for (let a = 0; a < 3; a++) {
        positions[i * 3 + a] = data.readFloatLE(28 + (i * 3 + a) * 4);
        attributes[i * 5 + a] = data.readFloatLE(28 + count * 12 + (i * 3 + a) * 4);
    }
    for (let a = 0; a < 2; a++) attributes[i * 5 + 3 + a] = data.readFloatLE(28 + count * 24 + (i * 2 + a) * 4);
}
const indices = new Uint32Array(indexCount);
for (let i = 0; i < indexCount; i++) indices[i] = data.readUInt32LE(28 + count * 32 + i * 4);
const [lod, error] = simplifier.simplifyWithAttributes(indices, positions, 3, attributes, 5,
    [0.1, 0.1, 0.1, 0.25, 0.25], null, 1800 * 3, 0.035, ['Permissive', 'RegularizeLight']);
// Store original IDs, not compacted indices; the loader compacts and puts the
// source VAT index in TEXCOORD1. UVs and animation samples remain unchanged.
const output = Buffer.alloc(12 + lod.length * 4);
output.writeUInt32LE(0x474c4f44, 0);
output.writeUInt32LE(count, 4);
output.writeUInt32LE(lod.length, 8);
for (let i = 0; i < lod.length; i++) output.writeUInt32LE(lod[i], 12 + i * 4);
fs.writeFileSync(path.join(directory, 'RunningLod.bytes'), output);
console.log(JSON.stringify({ originalTriangles: indexCount / 3, triangles: lod.length / 3,
    vertices: new Set(lod).size, relativeError: error }));
