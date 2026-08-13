using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    public sealed class HordeEnemyManager : MonoBehaviour
    {
        private const int InstanceBatchSize = 1023;
        private const float RoadHalfWidth = 2.45f;
        private const float LaneSpacing = 0.38f;
        private const float VisualRadius = 0.28f;

        private readonly List<EnemyDefinition> spawnSequence = new();
        private readonly Matrix4x4[] matrixBatch = new Matrix4x4[InstanceBatchSize];
        private Vector3[] positions;
        private Vector3[] previousPositions;
        private float[] pathDistances;
        private float[] laneOffsets;
        private float[] speeds;
        private float[] spawnTimes;
        private int[] segmentIndices;
        private bool[] alive;
        private Vector3[] segmentStarts;
        private Vector3[] segmentDirections;
        private Vector3[] segmentSides;
        private float[] segmentStartDistances;
        private float[] segmentEndDistances;
        private WaveDefinition wave;
        private PathRoute path;
        private Mesh mesh;
        private Material material;
        private MaterialPropertyBlock properties;
        private float elapsed;
        private int totalSpawned;
        private int activeCount;
        private int totalResolved;
        private bool running;

        public int TotalSpawned => totalSpawned;
        public int ActiveCount => activeCount;
        public int TotalResolved => totalResolved;
        public bool IsRunning => running;
        public bool IsComplete => running && totalSpawned >= spawnSequence.Count && activeCount == 0;

        public void BeginWave(WaveDefinition waveDefinition, PathRoute route)
        {
            Clear();
            wave = waveDefinition;
            path = route;
            BuildSpawnSequence();
            if (wave == null || path == null || !path.HasUsableRoute || spawnSequence.Count == 0)
            {
                running = false;
                return;
            }

            var count = spawnSequence.Count;
            BuildRouteCache();
            if (segmentStarts == null || segmentStarts.Length == 0)
            {
                running = false;
                return;
            }

            positions = new Vector3[count];
            previousPositions = new Vector3[count];
            pathDistances = new float[count];
            laneOffsets = new float[count];
            speeds = new float[count];
            spawnTimes = new float[count];
            segmentIndices = new int[count];
            alive = new bool[count];
            var cursor = 0f;
            var windowDuration = Mathf.Max(0.01f, wave.spawnInterval);
            var minBurst = Mathf.Max(1, wave.randomSpawnBurstMin);
            var maxBurst = Mathf.Max(minBurst, wave.randomSpawnBurstMax);
            var burstIndex = 0;
            while (burstIndex < count)
            {
                var burst = Mathf.Min(count - burstIndex, Random.Range(minBurst, maxBurst + 1));
                for (var i = 0; i < burst; i++)
                {
                    spawnTimes[burstIndex + i] = cursor + windowDuration * i / burst;
                }

                burstIndex += burst;
                cursor += windowDuration;
            }

            mesh = EnemyManager.GetDetailedEnemyMesh();
            material = BootstrapMaterials.Get(new Color(0.1f, 0.9f, 0.18f, 1f));
            material.enableInstancing = true;
            properties ??= new MaterialPropertyBlock();
            elapsed = 0f;
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
            running = true;
        }

        public void StopWave()
        {
            Clear();
        }

        private void Clear()
        {
            running = false;
            wave = null;
            spawnSequence.Clear();
            positions = null;
            previousPositions = null;
            pathDistances = null;
            laneOffsets = null;
            speeds = null;
            spawnTimes = null;
            segmentIndices = null;
            alive = null;
            segmentStarts = null;
            segmentDirections = null;
            segmentSides = null;
            segmentStartDistances = null;
            segmentEndDistances = null;
            elapsed = 0f;
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
        }

        private void Update()
        {
            if (!running || wave == null || path == null)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            SpawnDueEnemies();
            Simulate(deltaTime);
        }

        private void LateUpdate()
        {
            if (!running || mesh == null || material == null || positions == null)
            {
                return;
            }

            DrawInstances();
        }

        private void SpawnDueEnemies()
        {
            while (totalSpawned < spawnSequence.Count && elapsed >= spawnTimes[totalSpawned])
            {
                var definition = spawnSequence[totalSpawned];
                var laneIndex = totalSpawned % 13 - 6;
                laneOffsets[totalSpawned] = Mathf.Clamp(laneIndex * LaneSpacing + Random.Range(-0.08f, 0.08f), -RoadHalfWidth + 0.25f, RoadHalfWidth - 0.25f);
                pathDistances[totalSpawned] = 0f;
                segmentIndices[totalSpawned] = 0;
                speeds[totalSpawned] = definition != null ? definition.speed : 4.8f;
                var position = SamplePosition(totalSpawned);
                positions[totalSpawned] = position;
                previousPositions[totalSpawned] = position;
                alive[totalSpawned] = true;
                totalSpawned++;
                activeCount++;
            }
        }

        private void Simulate(float deltaTime)
        {
            if (activeCount <= 0)
            {
                return;
            }

            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                previousPositions[i] = positions[i];
                pathDistances[i] += speeds[i] * deltaTime;
                if (pathDistances[i] >= path.TotalLength)
                {
                    alive[i] = false;
                    activeCount--;
                    totalResolved++;
                    continue;
                }

                AdvanceSegmentIndex(i);
                positions[i] = SamplePosition(i);
            }
        }

        private Vector3 SamplePosition(int index)
        {
            var distance = pathDistances[index];
            var segment = Mathf.Clamp(segmentIndices[index], 0, segmentStarts.Length - 1);
            var center = segmentStarts[segment] + segmentDirections[segment] * Mathf.Max(0f, distance - segmentStartDistances[segment]);
            var side = segmentSides[segment];
            var weave = Mathf.Sin(distance * 1.65f + index * 0.73f) * 0.16f;
            return center + side * Mathf.Clamp(laneOffsets[index] + weave, -RoadHalfWidth + 0.2f, RoadHalfWidth - 0.2f);
        }

        private void AdvanceSegmentIndex(int enemyIndex)
        {
            var segment = segmentIndices[enemyIndex];
            while (segment < segmentEndDistances.Length - 1 && pathDistances[enemyIndex] > segmentEndDistances[segment])
            {
                segment++;
            }

            segmentIndices[enemyIndex] = segment;
        }

        private void BuildRouteCache()
        {
            var points = path.Waypoints;
            if (points == null || points.Count < 2)
            {
                segmentStarts = null;
                return;
            }

            var segmentCount = points.Count - 1;
            segmentStarts = new Vector3[segmentCount];
            segmentDirections = new Vector3[segmentCount];
            segmentSides = new Vector3[segmentCount];
            segmentStartDistances = new float[segmentCount];
            segmentEndDistances = new float[segmentCount];
            var distance = 0f;
            for (var i = 0; i < segmentCount; i++)
            {
                var from = points[i];
                var to = points[i + 1];
                var delta = to - from;
                delta.y = 0f;
                var length = Mathf.Max(0.001f, delta.magnitude);
                var direction = delta / length;
                segmentStarts[i] = from;
                segmentDirections[i] = direction;
                segmentSides[i] = Vector3.Cross(Vector3.up, direction);
                segmentStartDistances[i] = distance;
                distance += length;
                segmentEndDistances[i] = distance;
            }
        }

        private void DrawInstances()
        {
            var camera = Camera.main;
            var batchCount = 0;
            var scale = Vector3.one * VisualRadius;
            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                if (camera != null && !IsVisible(camera, positions[i]))
                {
                    continue;
                }

                matrixBatch[batchCount++] = Matrix4x4.TRS(positions[i], Quaternion.identity, scale);
                if (batchCount >= InstanceBatchSize)
                {
                    FlushBatch(batchCount);
                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                FlushBatch(batchCount);
            }
        }

        private static bool IsVisible(Camera camera, Vector3 position)
        {
            var viewport = camera.WorldToViewportPoint(position);
            return viewport.z > 0f && viewport.x > -0.08f && viewport.x < 1.08f && viewport.y > -0.08f && viewport.y < 1.08f;
        }

        private void FlushBatch(int count)
        {
            Graphics.DrawMeshInstanced(mesh, 0, material, matrixBatch, count, properties, ShadowCastingMode.Off, false, gameObject.layer);
        }

        private void BuildSpawnSequence()
        {
            spawnSequence.Clear();
            if (wave?.entries == null)
            {
                return;
            }

            for (var i = 0; i < wave.entries.Length && spawnSequence.Count < wave.totalEnemyCount; i++)
            {
                var entry = wave.entries[i];
                if (entry.enemy == null || entry.count <= 0)
                {
                    continue;
                }

                var remaining = wave.totalEnemyCount - spawnSequence.Count;
                var count = Mathf.Min(entry.count, remaining);
                for (var j = 0; j < count; j++)
                {
                    spawnSequence.Add(entry.enemy);
                }
            }
        }
    }
}
