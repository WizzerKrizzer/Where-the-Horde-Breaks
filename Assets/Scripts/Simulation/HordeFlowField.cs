using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Simulation
{
    /// <summary>
    /// A compact, immutable flow field made from the union of one or more road polylines.
    /// Runtime agents only sample this grid; no per-agent path search is performed.
    /// </summary>
    public sealed class HordeFlowField
    {
        private const float Unreachable = float.PositiveInfinity;
        private static readonly Vector2Int[] Neighbors =
        {
            new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
            new(-1, -1), new(-1, 1), new(1, -1), new(1, 1)
        };

        private readonly Vector3 origin;
        private readonly float cellSize;
        private readonly float corridorHalfWidth;
        private readonly int width;
        private readonly int height;
        private readonly bool[] walkable;
        private readonly float[] integration;
        private readonly float[] potential;
        private readonly Vector2[] directions;
        private readonly Vector2[] corridorDirections;
        private readonly float[] corridorDirectionDistanceSq;
        private readonly byte[] clearance;
        private readonly Vector3 primaryStart;
        private readonly Vector3 startForward;
        private readonly Vector3 exitForward;
        private readonly float startHalfWidth;
        private readonly float exitHalfWidth;

        public Vector3 Exit { get; }
        public float CellSize => cellSize;
        public Vector3 Origin => origin;
        public int Width => width;
        public int Height => height;

        public HordeFlowField(
            IReadOnlyList<Vector3> primaryRoute,
            IReadOnlyList<Vector3> secondaryRoute,
            float roadHalfWidth,
            float requestedCellSize = 0.65f,
            IReadOnlyList<float> primaryWaypointHalfWidths = null,
            IReadOnlyList<float> secondaryWaypointHalfWidths = null)
        {
            if (primaryRoute == null || primaryRoute.Count < 2)
            {
                throw new ArgumentException("A flow field needs at least two primary route points.", nameof(primaryRoute));
            }

            cellSize = Mathf.Max(0.25f, requestedCellSize);
            corridorHalfWidth = Mathf.Max(
                cellSize,
                Mathf.Max(
                    GetMaximumHalfWidth(primaryWaypointHalfWidths, roadHalfWidth),
                    GetMaximumHalfWidth(secondaryWaypointHalfWidths, roadHalfWidth)));
            primaryStart = primaryRoute[0];
            startForward = FlatDirection(primaryRoute[0], primaryRoute[1]);
            startHalfWidth = GetWaypointHalfWidth(primaryWaypointHalfWidths, 0, roadHalfWidth);
            Exit = primaryRoute[^1];
            exitForward = FlatDirection(primaryRoute[^2], primaryRoute[^1]);
            exitHalfWidth = GetWaypointHalfWidth(primaryWaypointHalfWidths, primaryRoute.Count - 1, roadHalfWidth);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            IncludeBounds(primaryRoute, ref min, ref max);
            IncludeBounds(secondaryRoute, ref min, ref max);
            var padding = corridorHalfWidth + cellSize * 2f;
            min -= Vector2.one * padding;
            max += Vector2.one * padding;
            origin = new Vector3(min.x, 0f, min.y);
            width = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cellSize));
            height = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / cellSize));
            walkable = new bool[width * height];
            integration = new float[walkable.Length];
            potential = new float[walkable.Length];
            directions = new Vector2[walkable.Length];
            corridorDirections = new Vector2[walkable.Length];
            corridorDirectionDistanceSq = new float[walkable.Length];
            Array.Fill(corridorDirectionDistanceSq, float.PositiveInfinity);
            clearance = new byte[walkable.Length];

            RasterizeRoute(primaryRoute, roadHalfWidth, primaryWaypointHalfWidths);
            RasterizeRoute(secondaryRoute, roadHalfWidth, secondaryWaypointHalfWidths);
            BuildClearance();
            BuildIntegration();
            BuildPotential();
            BuildDirections();
        }

        public bool IsWalkable(Vector3 worldPosition)
        {
            return TryWorldToCell(worldPosition, out var x, out var y) && walkable[Index(x, y)];
        }

        public Vector3 GetDirection(Vector3 worldPosition)
        {
            if (!TryWorldToCell(worldPosition, out var x, out var y))
            {
                return exitForward;
            }

            if (integration[Index(x, y)] <= 0.001f)
            {
                return exitForward;
            }

            // Interpolate cell vectors so agents do not visibly switch between eight grid headings.
            var gridX = (worldPosition.x - origin.x) / cellSize - 0.5f;
            var gridY = (worldPosition.z - origin.z) / cellSize - 0.5f;
            var x0 = Mathf.FloorToInt(gridX);
            var y0 = Mathf.FloorToInt(gridY);
            var tx = gridX - x0;
            var ty = gridY - y0;
            var direction =
                SampleDirection(x0, y0) * ((1f - tx) * (1f - ty)) +
                SampleDirection(x0 + 1, y0) * (tx * (1f - ty)) +
                SampleDirection(x0, y0 + 1) * ((1f - tx) * ty) +
                SampleDirection(x0 + 1, y0 + 1) * (tx * ty);
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = directions[Index(x, y)];
                if (direction.sqrMagnitude < 0.001f)
                {
                    return exitForward;
                }
            }

            direction.Normalize();
            return FindNavigableDirection(worldPosition, new Vector3(direction.x, 0f, direction.y));
        }

        public float GetDistanceToExit(Vector3 worldPosition)
        {
            if (!TryWorldToCell(worldPosition, out var x, out var y))
            {
                return Vector3.Distance(worldPosition, Exit);
            }

            var value = integration[Index(x, y)];
            return float.IsInfinity(value) ? Vector3.Distance(worldPosition, Exit) : value;
        }

        public bool HasReachedExit(Vector3 worldPosition)
        {
            return TryWorldToCell(worldPosition, out var x, out var y) && integration[Index(x, y)] <= 0.001f;
        }

        /// <summary>
        /// Returns an inward wall force sampled from the grid's distance transform.
        /// It behaves like a low-resolution SDF gradient and is zero away from walls.
        /// </summary>
        public Vector3 GetWallRepulsion(Vector3 worldPosition, float influenceDistance)
        {
            if (influenceDistance <= 0f || !TryWorldToCell(worldPosition, out var x, out var y))
            {
                return Vector3.zero;
            }

            var distance = SampleClearanceDistance(x, y);
            if (distance >= influenceDistance)
            {
                return Vector3.zero;
            }

            var gradient = new Vector3(
                SampleClearanceDistance(x + 1, y) - SampleClearanceDistance(x - 1, y),
                0f,
                SampleClearanceDistance(x, y + 1) - SampleClearanceDistance(x, y - 1));
            if (gradient.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            var penetration = 1f - Mathf.Clamp01(distance / influenceDistance);
            return gradient.normalized * penetration;
        }

        public void BuildGpuData(out Vector4[] vectors, out Vector4[] cellData)
        {
            vectors = new Vector4[walkable.Length];
            cellData = new Vector4[walkable.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = Index(x, y);
                    var center = potential[index];
                    var wallGradient = new Vector2(
                        SampleClearanceDistance(x + 1, y) - SampleClearanceDistance(x - 1, y),
                        SampleClearanceDistance(x, y + 1) - SampleClearanceDistance(x, y - 1));
                    if (wallGradient.sqrMagnitude > 0.0001f)
                    {
                        wallGradient.Normalize();
                    }

                    vectors[index] = new Vector4(directions[index].x, directions[index].y, wallGradient.x, wallGradient.y);
                    var integrationValue = !walkable[index] || float.IsInfinity(integration[index])
                        ? -1f
                        : integration[index];
                    cellData[index] = new Vector4(
                        SampleClearanceDistance(x, y),
                        integrationValue,
                        potential[index],
                        walkable[index] ? 1f : 0f);
                }
            }
        }

        public Vector3 ConstrainMove(Vector3 current, Vector3 desired)
        {
            desired.y = current.y;
            var delta = desired - current;
            var steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / (cellSize * 0.45f)));
            var step = delta / steps;
            var result = current;
            for (var i = 0; i < steps; i++)
            {
                result = ConstrainStep(result, result + step);
            }

            return result;
        }

        private Vector3 ConstrainStep(Vector3 current, Vector3 desired)
        {
            if (IsWalkable(desired))
            {
                return desired;
            }

            var displacement = desired - current;
            var inwardNormal = GetWallRepulsion(current, cellSize * 2f);
            if (inwardNormal.sqrMagnitude > 0.0001f)
            {
                inwardNormal.Normalize();
                var outwardAmount = Mathf.Min(0f, Vector3.Dot(displacement, inwardNormal));
                var slide = current + displacement - inwardNormal * outwardAmount;
                if (IsWalkable(slide))
                {
                    return slide;
                }
            }

            // Sliding each axis independently gives a cheap, stable wall collision response.
            var xOnly = new Vector3(desired.x, current.y, current.z);
            var zOnly = new Vector3(current.x, current.y, desired.z);
            var xValid = IsWalkable(xOnly);
            var zValid = IsWalkable(zOnly);
            if (xValid && zValid)
            {
                return (xOnly - current).sqrMagnitude >= (zOnly - current).sqrMagnitude ? xOnly : zOnly;
            }

            if (xValid)
            {
                return xOnly;
            }

            if (zValid)
            {
                return zOnly;
            }

            return current;
        }

        public Vector3 GetSpawnPoint(float lateral01)
        {
            var start = FindNearestWalkable(primaryStart: true);
            var forward = GetDirection(start);
            var side = Vector3.Cross(Vector3.up, forward).normalized;
            var candidate = start + side * lateral01;
            return IsWalkable(candidate) ? candidate : start;
        }

        private Vector3 FindNearestWalkable(bool primaryStart)
        {
            var target = primaryStart ? this.primaryStart : Exit;
            if (TryWorldToCell(target, out var targetX, out var targetY) && IsWalkableCell(targetX, targetY))
            {
                return CellCenter(targetX, targetY);
            }

            return target;
        }

        private void RasterizeRoute(
            IReadOnlyList<Vector3> route,
            float defaultRadius,
            IReadOnlyList<float> waypointHalfWidths)
        {
            if (route == null || route.Count < 2)
            {
                return;
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var point = CellCenter(x, y);
                    for (var segment = 1; segment < route.Count; segment++)
                    {
                        var distanceSq = DistanceToSegmentSquared(
                            point,
                            route[segment - 1],
                            route[segment],
                            out var segmentT);
                        var segmentRadius = Mathf.Lerp(
                            GetWaypointHalfWidth(waypointHalfWidths, segment - 1, defaultRadius),
                            GetWaypointHalfWidth(waypointHalfWidths, segment, defaultRadius),
                            segmentT);
                        var radiusSq = segmentRadius * segmentRadius;
                        if (distanceSq > radiusSq)
                        {
                            continue;
                        }

                        var index = Index(x, y);
                        walkable[index] = true;
                        if (distanceSq < corridorDirectionDistanceSq[index])
                        {
                            corridorDirectionDistanceSq[index] = distanceSq;
                            corridorDirections[index] = GetBlendedRouteDirection(route, segment, segmentT, segmentRadius);
                        }
                    }
                }
            }
        }

        private void BuildClearance()
        {
            var queue = new Queue<int>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = Index(x, y);
                    if (!walkable[index])
                    {
                        continue;
                    }

                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1 ||
                        !IsWalkableCell(x - 1, y) || !IsWalkableCell(x + 1, y) ||
                        !IsWalkableCell(x, y - 1) || !IsWalkableCell(x, y + 1))
                    {
                        clearance[index] = 1;
                        queue.Enqueue(index);
                    }
                }
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % width;
                var y = index / width;
                for (var n = 0; n < 4; n++)
                {
                    var nx = x + Neighbors[n].x;
                    var ny = y + Neighbors[n].y;
                    if (!IsWalkableCell(nx, ny))
                    {
                        continue;
                    }

                    var next = Index(nx, ny);
                    var nextClearance = (byte)Mathf.Min(byte.MaxValue, clearance[index] + 1);
                    if (clearance[next] == 0 || nextClearance < clearance[next])
                    {
                        clearance[next] = nextClearance;
                        queue.Enqueue(next);
                    }
                }
            }
        }

        private void BuildIntegration()
        {
            Array.Fill(integration, Unreachable);
            var heap = new MinHeap(integration.Length);
            // Seed a full cross-section, not a single exit point. This prevents every
            // stream from converging into one line as it approaches the end of the road.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = Index(x, y);
                    if (!walkable[index])
                    {
                        continue;
                    }

                    var fromExit = CellCenter(x, y) - Exit;
                    fromExit.y = 0f;
                    var longitudinal = Vector3.Dot(fromExit, exitForward);
                    var lateral = fromExit - exitForward * longitudinal;
                    if (Mathf.Abs(longitudinal) > cellSize * 0.7f ||
                        lateral.sqrMagnitude > exitHalfWidth * exitHalfWidth)
                    {
                        continue;
                    }

                    integration[index] = 0f;
                    directions[index] = new Vector2(exitForward.x, exitForward.z);
                    heap.Push(index, 0f);
                }
            }

            if (heap.Count == 0 && TryWorldToCell(Exit, out var exitX, out var exitY) && IsWalkableCell(exitX, exitY))
            {
                var exitIndex = Index(exitX, exitY);
                integration[exitIndex] = 0f;
                heap.Push(exitIndex, 0f);
            }

            while (heap.Count > 0)
            {
                heap.Pop(out var current, out var currentCost);
                if (currentCost > integration[current] + 0.0001f)
                {
                    continue;
                }

                var x = current % width;
                var y = current / width;
                for (var n = 0; n < Neighbors.Length; n++)
                {
                    var offset = Neighbors[n];
                    var nx = x + offset.x;
                    var ny = y + offset.y;
                    if (!IsWalkableCell(nx, ny) || IsBlockedDiagonal(x, y, offset))
                    {
                        continue;
                    }

                    var next = Index(nx, ny);
                    var step = (offset.x == 0 || offset.y == 0) ? cellSize : cellSize * 1.41421356f;
                    // Only discourage direct wall contact. A larger clearance penalty
                    // creates a single central river and wastes most of the road width.
                    var wallCost = clearance[next] <= 1 ? 1.08f : 1f;
                    var candidate = currentCost + step * wallCost;
                    if (candidate >= integration[next])
                    {
                        continue;
                    }

                    integration[next] = candidate;
                    heap.Push(next, candidate);
                }
            }
        }

        private void BuildDirections()
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = Index(x, y);
                    if (!walkable[index] || float.IsInfinity(integration[index]))
                    {
                        continue;
                    }

                    if (integration[index] <= 0.001f)
                    {
                        directions[index] = new Vector2(exitForward.x, exitForward.z);
                        continue;
                    }

                    // Very wide stress corridors need a longitudinal vector in every
                    // cell. A partially converged harmonic solve can contain zero or
                    // strongly sideways vectors on grids hundreds of cells across,
                    // which funnels the whole horde into a triangular stream. The
                    // precomputed corridor tangent is still a shared flow field (not
                    // per-agent lane/path-distance steering) and leaves lateral motion
                    // entirely to collision and density pressure.
                    if (corridorHalfWidth >= cellSize * 8f &&
                        corridorDirections[index].sqrMagnitude > 0.000001f)
                    {
                        directions[index] = corridorDirections[index].normalized;
                        continue;
                    }

                    // Negative gradient of a harmonic potential. Unlike a shortest-path
                    // gradient, its streamlines distribute across the entire corridor.
                    var center = potential[index];
                    var left = GetPotentialOrSelf(x - 1, y, center);
                    var right = GetPotentialOrSelf(x + 1, y, center);
                    var down = GetPotentialOrSelf(x, y - 1, center);
                    var up = GetPotentialOrSelf(x, y + 1, center);
                    var direction = new Vector2(left - right, down - up);
                    if (direction.sqrMagnitude > 0.000001f)
                    {
                        directions[index] = direction.normalized;
                    }
                    else
                    {
                        directions[index] = GetIntegrationDirection(x, y);
                    }
                }
            }
        }

        private Vector2 GetIntegrationDirection(int x, int y)
        {
            var current = integration[Index(x, y)];
            var bestScore = 0f;
            var bestDirection = Vector2.zero;
            for (var i = 0; i < Neighbors.Length; i++)
            {
                var offset = Neighbors[i];
                var nx = x + offset.x;
                var ny = y + offset.y;
                if (!IsWalkableCell(nx, ny) || IsBlockedDiagonal(x, y, offset))
                {
                    continue;
                }

                var neighborCost = integration[Index(nx, ny)];
                if (float.IsInfinity(neighborCost))
                {
                    continue;
                }

                var stepLength = offset.x == 0 || offset.y == 0 ? 1f : 1.41421356f;
                var score = (current - neighborCost) / stepLength;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestDirection = new Vector2(offset.x, offset.y).normalized;
            }

            return bestDirection;
        }

        private void BuildPotential()
        {
            var fixedCells = new bool[walkable.Length];
            var maxDistance = 0f;
            for (var i = 0; i < integration.Length; i++)
            {
                if (walkable[i] && !float.IsInfinity(integration[i]))
                {
                    maxDistance = Mathf.Max(maxDistance, integration[i]);
                }
            }

            maxDistance = Mathf.Max(cellSize, maxDistance);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = Index(x, y);
                    if (!walkable[index] || float.IsInfinity(integration[index]))
                    {
                        continue;
                    }

                    potential[index] = Mathf.Clamp01(integration[index] / maxDistance);
                    if (integration[index] <= 0.001f)
                    {
                        potential[index] = 0f;
                        fixedCells[index] = true;
                        continue;
                    }

                    var fromStart = CellCenter(x, y) - primaryStart;
                    fromStart.y = 0f;
                    var longitudinal = Vector3.Dot(fromStart, startForward);
                    var lateral = fromStart - startForward * longitudinal;
                    if (Mathf.Abs(longitudinal) <= cellSize * 0.7f &&
                        lateral.sqrMagnitude <= startHalfWidth * startHalfWidth)
                    {
                        potential[index] = 1f;
                        fixedCells[index] = true;
                    }
                }
            }

            // Gauss-Seidel relaxation solves Laplace's equation with no-flow walls.
            // This is a one-time wave-start cost; runtime sampling remains O(1).
            var iterations = Mathf.Clamp(Mathf.Max(width, height) * 5, 120, 420);
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var reverse = (iteration & 1) != 0;
                for (var row = 0; row < height; row++)
                {
                    var y = reverse ? height - 1 - row : row;
                    for (var column = 0; column < width; column++)
                    {
                        var x = reverse ? width - 1 - column : column;
                        var index = Index(x, y);
                        if (!walkable[index] || fixedCells[index] || float.IsInfinity(integration[index]))
                        {
                            continue;
                        }

                        var current = potential[index];
                        potential[index] = (
                            GetPotentialOrSelf(x - 1, y, current) +
                            GetPotentialOrSelf(x + 1, y, current) +
                            GetPotentialOrSelf(x, y - 1, current) +
                            GetPotentialOrSelf(x, y + 1, current)) * 0.25f;
                    }
                }
            }
        }

        private float GetPotentialOrSelf(int x, int y, float self)
        {
            return IsWalkableCell(x, y) && !float.IsInfinity(integration[Index(x, y)])
                ? potential[Index(x, y)]
                : self;
        }

        private bool IsBlockedDiagonal(int x, int y, Vector2Int offset)
        {
            return offset.x != 0 && offset.y != 0 &&
                   (!IsWalkableCell(x + offset.x, y) || !IsWalkableCell(x, y + offset.y));
        }

        private bool TryWorldToCell(Vector3 position, out int x, out int y)
        {
            x = Mathf.FloorToInt((position.x - origin.x) / cellSize);
            y = Mathf.FloorToInt((position.z - origin.z) / cellSize);
            return x >= 0 && y >= 0 && x < width && y < height;
        }

        private bool IsWalkableCell(int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height && walkable[Index(x, y)];
        }

        private Vector2 SampleDirection(int x, int y)
        {
            return IsWalkableCell(x, y) ? directions[Index(x, y)] : Vector2.zero;
        }

        private float SampleClearanceDistance(int x, int y)
        {
            if (!IsWalkableCell(x, y))
            {
                return 0f;
            }

            return Mathf.Max(0f, clearance[Index(x, y)] - 0.5f) * cellSize;
        }

        private Vector3 FindNavigableDirection(Vector3 position, Vector3 preferred)
        {
            var probeDistance = cellSize * 0.7f;
            if (IsWalkable(position + preferred * probeDistance))
            {
                return preferred;
            }

            var currentPotential = GetPotential(position);
            var best = Vector3.zero;
            var bestScore = float.NegativeInfinity;
            // Test progressively wider turns on both sides. This projects a flow vector
            // onto the local wall tangent instead of letting agents push into the wall.
            for (var step = 1; step <= 4; step++)
            {
                var angle = step * 22.5f;
                EvaluateDirection(Quaternion.Euler(0f, angle, 0f) * preferred);
                EvaluateDirection(Quaternion.Euler(0f, -angle, 0f) * preferred);
            }

            return best.sqrMagnitude > 0.001f ? best : preferred;

            void EvaluateDirection(Vector3 candidate)
            {
                var probe = position + candidate * probeDistance;
                if (!IsWalkable(probe))
                {
                    return;
                }

                var progress = currentPotential - GetPotential(probe);
                var score = progress * 6f + Vector3.Dot(candidate, preferred) * 0.35f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        private float GetPotential(Vector3 worldPosition)
        {
            return TryWorldToCell(worldPosition, out var x, out var y)
                ? potential[Index(x, y)]
                : 0f;
        }

        private int Index(int x, int y) => y * width + x;

        private Vector3 CellCenter(int x, int y)
        {
            return new Vector3(origin.x + (x + 0.5f) * cellSize, 0f, origin.z + (y + 0.5f) * cellSize);
        }

        private static void IncludeBounds(IReadOnlyList<Vector3> route, ref Vector2 min, ref Vector2 max)
        {
            if (route == null)
            {
                return;
            }

            for (var i = 0; i < route.Count; i++)
            {
                var point = new Vector2(route[i].x, route[i].z);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        private static float DistanceToSegmentSquared(Vector3 point, Vector3 from, Vector3 to, out float segmentT)
        {
            point.y = from.y = to.y = 0f;
            var segment = to - from;
            var lengthSq = segment.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                segmentT = 0f;
                return (point - from).sqrMagnitude;
            }

            segmentT = Mathf.Clamp01(Vector3.Dot(point - from, segment) / lengthSq);
            return (point - (from + segment * segmentT)).sqrMagnitude;
        }

        private static Vector2 GetBlendedRouteDirection(
            IReadOnlyList<Vector3> route,
            int segment,
            float segmentT,
            float blendDistance)
        {
            var current = FlatDirection(route[segment - 1], route[segment]);
            var current2 = new Vector2(current.x, current.z);
            var segmentLength = Vector3.Distance(route[segment - 1], route[segment]);
            var blendT = Mathf.Clamp(blendDistance / Mathf.Max(segmentLength, 0.001f), 0.08f, 0.45f);

            if (segmentT < blendT && segment > 1)
            {
                var previous = FlatDirection(route[segment - 2], route[segment - 1]);
                var previous2 = new Vector2(previous.x, previous.z);
                return Vector2.Lerp(previous2, current2, Mathf.SmoothStep(0f, 1f, segmentT / blendT)).normalized;
            }

            if (segmentT > 1f - blendT && segment < route.Count - 1)
            {
                var next = FlatDirection(route[segment], route[segment + 1]);
                var next2 = new Vector2(next.x, next.z);
                return Vector2.Lerp(current2, next2, Mathf.SmoothStep(0f, 1f, (segmentT - (1f - blendT)) / blendT)).normalized;
            }

            return current2;
        }

        private static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            var direction = to - from;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        }

        private static float GetWaypointHalfWidth(IReadOnlyList<float> widths, int index, float fallback)
        {
            return widths != null && index >= 0 && index < widths.Count
                ? Mathf.Max(0.5f, widths[index])
                : Mathf.Max(0.5f, fallback);
        }

        private static float GetMaximumHalfWidth(IReadOnlyList<float> widths, float fallback)
        {
            var maximum = Mathf.Max(0.5f, fallback);
            if (widths == null)
            {
                return maximum;
            }

            for (var i = 0; i < widths.Count; i++)
            {
                maximum = Mathf.Max(maximum, widths[i]);
            }

            return maximum;
        }

        private sealed class MinHeap
        {
            private readonly List<Node> nodes;
            public int Count => nodes.Count;

            public MinHeap(int capacity) => nodes = new List<Node>(capacity);

            public void Push(int index, float cost)
            {
                nodes.Add(new Node(index, cost));
                var child = nodes.Count - 1;
                while (child > 0)
                {
                    var parent = (child - 1) / 2;
                    if (nodes[parent].Cost <= cost)
                    {
                        break;
                    }

                    nodes[child] = nodes[parent];
                    child = parent;
                }

                nodes[child] = new Node(index, cost);
            }

            public void Pop(out int index, out float cost)
            {
                var root = nodes[0];
                var tail = nodes[^1];
                nodes.RemoveAt(nodes.Count - 1);
                if (nodes.Count > 0)
                {
                    var parent = 0;
                    while (true)
                    {
                        var left = parent * 2 + 1;
                        if (left >= nodes.Count)
                        {
                            break;
                        }

                        var right = left + 1;
                        var child = right < nodes.Count && nodes[right].Cost < nodes[left].Cost ? right : left;
                        if (nodes[child].Cost >= tail.Cost)
                        {
                            break;
                        }

                        nodes[parent] = nodes[child];
                        parent = child;
                    }

                    nodes[parent] = tail;
                }

                index = root.Index;
                cost = root.Cost;
            }

            private readonly struct Node
            {
                public readonly int Index;
                public readonly float Cost;
                public Node(int index, float cost) { Index = index; Cost = cost; }
            }
        }
    }
}
