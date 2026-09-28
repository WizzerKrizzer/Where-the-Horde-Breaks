using System.Collections.Generic;
using System.Threading.Tasks;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// One continuous road/terrain material, plus one exterior kerb mesh.
    /// Baked only when loading a map; no updates, colliders or per-segment draws.
    /// The footprint follows the same variable-radius/square-joint rules as
    /// HordeFlowField, including the union of all three routes at junctions.
    /// </summary>
    public sealed class RoadSurface : MonoBehaviour
    {
        private readonly List<Object> owned = new();
        private readonly List<Segment> segments = new();
        private readonly List<Corner> corners = new();
        private Texture2D field;
        private Rect bounds;
        private EnvironmentTilePackage environmentPackage;
        public Rect WorldBounds => bounds;
        public EnvironmentTilePackageId PackageId => environmentPackage?.Id ?? EnvironmentTilePackageId.Prehistoric;
        // The clean-board presentation deliberately has no paint, ruts or road
        // markings, regardless of the level's historical environment package.
        public bool UsesRoadMarkings => false;

        private struct Segment
        {
            public Vector2 from, direction;
            public float length, fromRadius, toRadius, along;
            public bool trimStart, trimEnd, paint;
        }

        private struct Corner
        {
            public Vector2 center, incoming, outgoing;
            public float incomingRadius, outgoingRadius;
        }

        public static RoadSurface Create(Transform parent, PathRoute route, LevelDefinition level)
        {
            var root = new GameObject("RoadSurface_Continuous");
            root.transform.SetParent(parent, false);
            var surface = root.AddComponent<RoadSurface>();
            surface.environmentPackage = EnvironmentTilePackages.Resolve(level != null
                ? level.environmentTilePackage
                : EnvironmentTilePackageId.Prehistoric);
            surface.AddRoute(route.Waypoints, route.WaypointHalfWidths, route.UseSquareCorners);
            surface.AddRoute(route.SecondaryWaypoints, route.SecondaryWaypointHalfWidths, route.UseSquareCorners);
            surface.AddRoute(route.TertiaryWaypoints, route.TertiaryWaypointHalfWidths, route.UseSquareCorners);
            var center = level != null ? level.groundCenter : new Vector3(0, 0, 1.5f);
            var size = level != null ? level.groundSize : new Vector3(82, .1f, 50);
            var min = new Vector2(center.x - size.x * .5f, center.z - size.z * .5f);
            var max = new Vector2(center.x + size.x * .5f, center.z + size.z * .5f);
            foreach (var segment in surface.segments)
            {
                var padding = Vector2.one * (Mathf.Max(segment.fromRadius, segment.toRadius) + 2);
                min = Vector2.Min(min, Vector2.Min(segment.from, segment.from + segment.direction * segment.length) - padding);
                max = Vector2.Max(max, Vector2.Max(segment.from, segment.from + segment.direction * segment.length) + padding);
            }
            surface.bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            surface.Build();
            return surface;
        }

        private static Vector2 Flat(Vector3 value) => new(value.x, value.z);
        private static float SmoothRange(float low, float high, float value) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(low, high, value));
        private static float Alignment(IReadOnlyList<Vector3> points, int index) =>
            index > 0 && index < points.Count - 1
                ? Vector2.Dot((Flat(points[index]) - Flat(points[index - 1])).normalized,
                    (Flat(points[index + 1]) - Flat(points[index])).normalized)
                : -1;
        private static bool SquareJoint(IReadOnlyList<Vector3> points, int i) =>
            Mathf.Abs(Alignment(points, i)) < .01f || Alignment(points, i) > .999f;

        private void AddRoute(IReadOnlyList<Vector3> points, IReadOnlyList<float> radii, bool square)
        {
            if (points == null || points.Count < 2) return;
            var along = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var delta = Flat(points[i] - points[i - 1]);
                var length = delta.magnitude;
                if (length < .001f) continue;
                var fromRadius = radii.Count == points.Count ? radii[i - 1] : 2.7f;
                var toRadius = radii.Count == points.Count ? radii[i] : 2.7f;
                if (Mathf.Abs(Alignment(points, i)) < .01f && Mathf.Abs(fromRadius - toRadius) > .001f)
                    toRadius = fromRadius;
                segments.Add(new Segment
                {
                    from = Flat(points[i - 1]), direction = delta / length, length = length,
                    fromRadius = fromRadius, toRadius = toRadius, along = along,
                    trimStart = square && SquareJoint(points, i - 1),
                    trimEnd = square && SquareJoint(points, i),
                    paint = Mathf.Max(fromRadius, toRadius) <= 4.5f
                });
                along += length;
            }
            if (!square) return;
            for (var i = 1; i < points.Count - 1; i++)
            {
                if (Mathf.Abs(Alignment(points, i)) >= .01f) continue;
                corners.Add(new Corner
                {
                    center = Flat(points[i]), incoming = Flat(points[i] - points[i - 1]).normalized,
                    outgoing = Flat(points[i + 1] - points[i]).normalized,
                    incomingRadius = radii.Count == points.Count ? radii[i - 1] : 2.7f,
                    outgoingRadius = radii.Count == points.Count ? radii[i] : 2.7f
                });
            }
        }

        // Positive inside. Kept analytic for validation and contour generation.
        public float SignedDistance(Vector2 p)
        {
            var result = -10000f;
            foreach (var segment in segments) result = Mathf.Max(result, Distance(segment, p, out _, out _));
            foreach (var corner in corners)
            {
                var offset = p - corner.center;
                result = Mathf.Max(result, Mathf.Min(
                    corner.outgoingRadius - Mathf.Abs(Vector2.Dot(offset, corner.incoming)),
                    corner.incomingRadius - Mathf.Abs(Vector2.Dot(offset, corner.outgoing))));
            }
            return result;
        }

        private static float Distance(Segment s, Vector2 p, out float rawAlong, out float lateral)
        {
            var offset = p - s.from;
            rawAlong = Vector2.Dot(offset, s.direction);
            lateral = Mathf.Abs(offset.x * s.direction.y - offset.y * s.direction.x);
            var t = Mathf.Clamp01(rawAlong / s.length);
            var radius = Mathf.Lerp(s.fromRadius, s.toRadius, t);
            if (s.trimStart && rawAlong < 0) return Mathf.Min(radius - lateral, rawAlong);
            if (s.trimEnd && rawAlong > s.length) return Mathf.Min(radius - lateral, s.length - rawAlong);
            return radius - (offset - s.direction * (t * s.length)).magnitude;
        }

        private void Build()
        {
            // Linear data, not colour. Eight-metre SDF range gives 3cm precision.
            var width = Mathf.Clamp(Mathf.CeilToInt(bounds.width * 12), 64, 2048);
            var height = Mathf.Clamp(Mathf.CeilToInt(bounds.height * 12), 64, 2048);
            field = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            { name = "Road footprint", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 1 };
            owned.Add(field);
            var pixels = new Color32[width * height];
            // Pure maths on worker threads; all Unity object creation/upload
            // remains on the main thread. No texture work during gameplay.
            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = 8 }, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var p = new Vector2(bounds.xMin + (x + .5f) / width * bounds.width, bounds.yMin + (y + .5f) / height * bounds.height);
                    var sdf = -10000f;
                    foreach (var s in segments)
                    {
                        sdf = Mathf.Max(sdf, Distance(s, p, out _, out _));
                    }
                    foreach (var corner in corners)
                    {
                        var offset = p - corner.center;
                        sdf = Mathf.Max(sdf, Mathf.Min(
                            corner.outgoingRadius - Mathf.Abs(Vector2.Dot(offset, corner.incoming)),
                            corner.incomingRadius - Mathf.Abs(Vector2.Dot(offset, corner.outgoing))));
                    }
                    // Only the signed road footprint is stored. The shader no
                    // longer receives markings, ruts or weathering data.
                    pixels[y * width + x] = new Color(Mathf.Clamp01(.5f + sdf / 8), 0f, 0f, 1f);
                }
            });
            field.SetPixels32(pixels);
            field.Apply(true, true);
            var shader = Resources.Load<Shader>("RoadSurface");
            var material = new Material(shader)
            {
                name = "Solid route surface"
            };
            material.SetTexture("_RoadField", field);
            material.SetVector("_WorldRect", new Vector4(bounds.xMin, bounds.yMin, bounds.width, bounds.height));
            owned.Add(material);
            var mesh = new Mesh { name = "Continuous road and terrain" };
            owned.Add(mesh);
            mesh.vertices = new[] { Point(bounds.xMin, bounds.yMin, .015f), Point(bounds.xMin, bounds.yMax, .015f),
                Point(bounds.xMax, bounds.yMax, .015f), Point(bounds.xMax, bounds.yMin, .015f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddRenderer(gameObject, mesh, material);
        }

        private static Vector3 Point(float x, float z, float y) => new(x, y, z);

        private void BuildKerb(Shader shader)
        {
            // Marching triangles extracts only the UNION boundary. Kerbs never
            // cut across junctions and stay outside the navigable footprint.
            const float step = .3f;
            var nx = Mathf.CeilToInt(bounds.width / step);
            var ny = Mathf.CeilToInt(bounds.height / step);
            var dx = bounds.width / nx;
            var dy = bounds.height / ny;
            var distances = new float[(nx + 1) * (ny + 1)];
            Parallel.For(0, ny + 1, new ParallelOptions { MaxDegreeOfParallelism = 8 }, y =>
            {
                for (var x = 0; x <= nx; x++)
                    distances[y * (nx + 1) + x] = SignedDistance(new Vector2(bounds.xMin + x * dx, bounds.yMin + y * dy)) + .025f;
            });
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var indices = new List<int>();
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
            {
                var a = new Vector2(bounds.xMin + x * dx, bounds.yMin + y * dy);
                var b = a + new Vector2(dx, 0);
                var c = a + new Vector2(dx, dy);
                var d = a + new Vector2(0, dy);
                var i = y * (nx + 1) + x;
                Contour(a, b, c, distances[i], distances[i + 1], distances[i + nx + 2], vertices, uv, indices);
                Contour(a, c, d, distances[i], distances[i + nx + 2], distances[i + nx + 1], vertices, uv, indices);
            }
            var mesh = new Mesh
            {
                name = "Route boundary",
                indexFormat = IndexFormat.UInt32
            };
            owned.Add(mesh);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var material = new Material(shader)
            {
                name = "Solid route boundary"
            };
            material.SetFloat("_Kerb", 1);
            material.SetFloat("_Prehistoric", 0f);
            owned.Add(material);
            var root = new GameObject("RouteBoundary");
            root.transform.SetParent(transform, false);
            AddRenderer(root, mesh, material);
        }

        private void Contour(Vector2 a, Vector2 b, Vector2 c, float da, float db, float dc,
            List<Vector3> vertices, List<Vector2> uv, List<int> indices)
        {
            var n = 0;
            var first = Vector2.zero;
            var second = Vector2.zero;
            Cross(a, b, da, db, ref n, ref first, ref second);
            Cross(b, c, db, dc, ref n, ref first, ref second);
            Cross(c, a, dc, da, ref n, ref first, ref second);
            if (n != 2 || (second - first).sqrMagnitude < .000001f) return;
            var tangent = (second - first).normalized;
            var firstOutside = OutsideNormal(first);
            var secondOutside = OutsideNormal(second);
            // Bevel rises away from the playable surface, not into it.
            var prehistoric = PackageId == EnvironmentTilePackageId.Prehistoric;
            var widths = prehistoric
                ? new[] { 0f, .10f, .31f, .48f }
                : new[] { 0f, .045f, .21f, .27f };
            var heights = prehistoric
                ? new[] { .017f, .052f, .047f, .012f }
                : new[] { .017f, .105f, .105f, .018f };
            for (var strip = 0; strip < 3; strip++)
            {
                var start = vertices.Count;
                var p0 = first + firstOutside * widths[strip];
                var p1 = second + secondOutside * widths[strip];
                var p2 = second + secondOutside * widths[strip + 1];
                var p3 = first + firstOutside * widths[strip + 1];
                vertices.Add(Point(p0.x, p0.y, heights[strip]));
                vertices.Add(Point(p1.x, p1.y, heights[strip]));
                vertices.Add(Point(p2.x, p2.y, heights[strip + 1]));
                vertices.Add(Point(p3.x, p3.y, heights[strip + 1]));
                var axis = Mathf.Abs(tangent.x) > Mathf.Abs(tangent.y) ? Vector2.right : Vector2.up;
                uv.Add(new Vector2(Vector2.Dot(p0, axis), widths[strip]));
                uv.Add(new Vector2(Vector2.Dot(p1, axis), widths[strip]));
                uv.Add(new Vector2(Vector2.Dot(p2, axis), widths[strip + 1]));
                uv.Add(new Vector2(Vector2.Dot(p3, axis), widths[strip + 1]));
                // Double-sided material handles winding along each contour.
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }
        }

        private Vector2 OutsideNormal(Vector2 p) => -new Vector2(
            SignedDistance(p + Vector2.right * .02f) - SignedDistance(p - Vector2.right * .02f),
            SignedDistance(p + Vector2.up * .02f) - SignedDistance(p - Vector2.up * .02f)).normalized;

        private static void Cross(Vector2 a, Vector2 b, float da, float db, ref int count, ref Vector2 first, ref Vector2 second)
        {
            if ((da >= 0) == (db >= 0)) return;
            var point = Vector2.Lerp(a, b, da / (da - db));
            if (count++ == 0) first = point; else second = point;
        }

        private static void AddRenderer(GameObject root, Mesh mesh, Material material)
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private void OnDestroy()
        {
            foreach (var item in owned) if (item != null) Destroy(item);
            owned.Clear();
        }
    }
}
