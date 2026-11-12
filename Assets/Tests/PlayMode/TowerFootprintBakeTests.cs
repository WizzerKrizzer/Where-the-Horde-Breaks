using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Measures the outline on the ground of each tower built from a model (user, 2026-11-11: every
    /// tower has a border from its real outline): the building is put up on a spot at the origin with
    /// the road ahead (+Z), and the convex hull of everything drawn, seen from above, is written as C#
    /// for TowerFootprints.cs (x across, y towards the road, metres from the tower's spot). Needs the
    /// local models (Tools/Animation/install_crew_models.sh, build_mammoth.sh, install_cave.sh) with
    /// readable meshes. Writes Logs/Reports/TowerFootprints/footprints.txt.
    /// </summary>
    public sealed class TowerFootprintBakeTests
    {
        private const string Output = "Logs/Reports/TowerFootprints";
        private const int MaxPoints = 20;

        [UnityTest, Explicit("Measures the towers' outlines from their models.")]
        public IEnumerator BakeTowerFootprints()
        {
            var report = new StringBuilder();
            var holder = new GameObject("Footprint spot");
            var tower = new GameObject("Tower").AddComponent<TowerActor>();
            tower.transform.SetParent(holder.transform, false);
            yield return null;

            Write(report, "slingers", SlingerTowerVisual.TryCreate(tower, Vector3.forward)?.gameObject);
            Write(report, "spear_thrower", SpearRockVisual.TryCreate(tower, Vector3.forward)?.gameObject);
            Write(report, "cave", CaveVisual.TryCreate(holder.transform, Vector3.zero, Vector3.forward, out _, out _));
            // The pen's gate stands 0.3 m out from the spot (BeastCharge.PenGateAhead).
            Write(report, "mammoth", MammothVisual.TryCreatePen(holder.transform, Vector3.forward * 0.3f, Vector3.forward));
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/footprints.txt", report.ToString());
            Debug.Log(report.ToString());
            Object.Destroy(holder);
        }

        private static void Write(StringBuilder report, string id, GameObject built)
        {
            if (built == null)
            {
                report.AppendLine($"// {id}: model not installed");
                return;
            }

            var points = new List<Vector2>();
            var baked = new Mesh();
            foreach (var renderer in built.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh;
                Matrix4x4 toWorld;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.BakeMesh(baked, true);
                    mesh = baked;
                    toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    mesh = filter.sharedMesh;
                    toWorld = renderer.transform.localToWorldMatrix;
                }

                foreach (var vertex in mesh.vertices)
                {
                    var world = toWorld.MultiplyPoint3x4(vertex);
                    points.Add(new Vector2(world.x, world.z));
                }
            }

            Object.Destroy(baked);
            var hull = Simplify(Hull(points), MaxPoints);
            var culture = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            foreach (var point in hull)
            {
                parts.Add(string.Format(culture, "new Vector2({0:F2}f, {1:F2}f)", point.x, point.y));
            }

            report.AppendLine($"            {{ \"{id}\", new[] {{ {string.Join(", ", parts)} }} }},");
            Object.Destroy(built);
        }

        // The convex hull, anticlockwise seen from above (monotone chain).
        private static List<Vector2> Hull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            var lower = hull.Count + 1;
            for (var i = points.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(points[i]);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        // Fewer corners: drops the corner that cuts away the least area until `count` remain. The
        // outline shrinks by a few centimetres at most.
        private static List<Vector2> Simplify(List<Vector2> hull, int count)
        {
            while (hull.Count > count)
            {
                var best = 0;
                var smallest = float.MaxValue;
                for (var i = 0; i < hull.Count; i++)
                {
                    var a = hull[(i + hull.Count - 1) % hull.Count];
                    var b = hull[i];
                    var c = hull[(i + 1) % hull.Count];
                    var area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                    if (area < smallest)
                    {
                        smallest = area;
                        best = i;
                    }
                }

                hull.RemoveAt(best);
            }

            return hull;
        }
    }
}
