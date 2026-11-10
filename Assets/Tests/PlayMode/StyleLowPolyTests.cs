using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Throwaway style test for the recommended direction: flat-shaded low-poly units with
    /// vertex colours only, hard drop shadows, a stepped walk cycle, one bold ground colour and
    /// a prehistoric tower and terrain. Renders close-ups, stills at 20 / 60 / 150 / 360 m and
    /// two short frame sequences. Writes PNGs to Logs/StyleLowPoly.
    /// </summary>
    public sealed class StyleLowPolyTests
    {
        internal const int Width = 1920;
        internal const int Height = 1080;
        private const float FieldOfView = 55f;
        private const float Pitch = 75f;
        private const float RoadAmplitude = 22f;
        private const float RoadWavelength = 80f;
        private const float HordeHalfWidth = 12f;
        private const float CycleRate = 1.9f;
        private const string Output = "Logs/StyleLowPoly";
        internal static readonly Color Ground = new Color(0.40f, 0.29f, 0.22f);
        internal static readonly Vector3 Target = new Vector3(0f, 0f, 9f);

        [UnityTest, Explicit("Low-poly style test")]
        [Category("Performance")]
        public IEnumerator RenderLowPolyStyle()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Output + "/clip-20m");
            Directory.CreateDirectory(Output + "/clip-150m");
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            Assert.That(unitShader, Is.Not.Null);
            Assert.That(groundShader, Is.Not.Null);

            var otherMainCameras = new List<Camera>();
            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera"))
                {
                    other.enabled = false;
                    otherMainCameras.Add(other);
                }
            }

            // The game bootstrap builds a level map in every test scene; hide it so only
            // the ground and units drawn here are visible.
            var hiddenRenderers = new List<Renderer>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }

            var cameraObject = new GameObject("Low-poly camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Ground;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2000f;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            camera.targetTexture = target;

            var groundMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f),
                    new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var ground = new GameObject("Low-poly ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(groundShader);
            groundMaterial.SetColor("_Color", Ground);
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var unitMaterial = new Material(unitShader) { enableInstancing = true };
            unitMaterial.SetFloat("_CycleRate", CycleRate);
            var staticMaterial = new Material(unitShader) { enableInstancing = true };
            staticMaterial.SetFloat("_Animate", 0f);

            var goblin = LowPolyMeshes.Goblin();
            var tower = LowPolyMeshes.Tower();
            var towerPositions = TowerPositions();
            var terrain = LowPolyMeshes.Terrain(RoadCenter, towerPositions);
            var crowd = BuildCrowd();
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            // Close-up: turntable on the top row, the four walk poses below.
            {
                PlaceCamera(camera, 7f, new Vector3(0f, 0f, 0f));
                Shader.SetGlobalFloat("_StyleTime", 0f);
                var matrices = new List<Matrix4x4>();
                var seeds = new List<float>();
                for (var i = 0; i < 8; i++)
                {
                    matrices.Add(Matrix(-5.6f + i * 1.6f, 1.2f, 0.9f, i * 45f));
                    seeds.Add(0f);
                }

                for (var i = 0; i < 4; i++)
                {
                    matrices.Add(Matrix(-3.3f + i * 2.2f, -2.2f, 0.9f, 90f));
                    seeds.Add(i / 4f);
                }

                DrawInstanced(goblin, unitMaterial, matrices, seeds);
                yield return Capture(camera, target, texture, Output + "/closeup-goblin.png");
            }

            // Tower close-up with a few runners for scale and the plateau edge behind it.
            {
                var towerPosition = towerPositions[0];
                PlaceCamera(camera, 16f, towerPosition + new Vector3(0f, 0f, -2f));
                Shader.SetGlobalFloat("_StyleTime", 0.4f);
                DrawStatic(tower, staticMaterial, towerPositions);
                Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                DrawCrowd(goblin, unitMaterial, crowd, 0.4f);
                yield return Capture(camera, target, texture, Output + "/closeup-tower.png");
            }

            foreach (var height in new[] { 20f, 60f, 150f, 360f })
            {
                PlaceCamera(camera, height, Target);
                Shader.SetGlobalFloat("_StyleTime", 0.4f);
                DrawStatic(tower, staticMaterial, towerPositions);
                Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                DrawCrowd(goblin, unitMaterial, crowd, 0.4f);
                yield return Capture(camera, target, texture, $"{Output}/scene-{height:F0}m.png");
            }

            // Frame sequences at 30 fps: bodies move every frame, poses are held.
            foreach (var height in new[] { 20f, 150f })
            {
                PlaceCamera(camera, height, Target);
                for (var frame = 0; frame < 60; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    DrawStatic(tower, staticMaterial, towerPositions);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    DrawCrowd(goblin, unitMaterial, crowd, time);
                    yield return Capture(camera, target, texture, $"{Output}/clip-{height:F0}m/frame_{frame:D3}.png");
                }
            }

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(unitMaterial);
            Object.DestroyImmediate(staticMaterial);
            Object.DestroyImmediate(goblin);
            Object.DestroyImmediate(tower);
            Object.DestroyImmediate(terrain);
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            foreach (var renderer in hiddenRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }

            foreach (var other in otherMainCameras)
            {
                if (other != null) other.enabled = true;
            }
        }

        internal struct Walker
        {
            public float StartX;
            public float Lane;
            public float Speed;
            public float Yaw;
            public float Scale;
            public float Seed;
        }

        internal static float RoadCenter(float x) => RoadAmplitude * Mathf.Sin(x / RoadWavelength);

        internal static List<Vector3> TowerPositions()
        {
            var list = new List<Vector3>();
            var xs = new[] { 8f, -70f, 60f, 150f, -160f, 240f, -250f, 330f, -340f };
            for (var i = 0; i < xs.Length; i++)
            {
                var side = i % 2 == 0 ? 1f : -1f;
                list.Add(new Vector3(xs[i], 0f, RoadCenter(xs[i]) + side * 15.5f));
            }

            return list;
        }

        // Uniform random positions along the road, about 1.6 bodies per square metre in the
        // middle and thinning towards the edges. Statuses come in clumps (one area effect hits
        // a group), encoded in the seed's integer part: 1 burning, 2 frozen.
        internal static List<Walker> BuildCrowd()
        {
            var list = new List<Walker>();
            var random = new System.Random(11);
            var count = (int)(960f * HordeHalfWidth * 2f * 1.6f);
            for (var i = 0; i < count; i++)
            {
                var x = -480f + (float)random.NextDouble() * 960f;
                var lane = ((float)random.NextDouble() * 2f - 1f) * HordeHalfWidth;
                var edge = Mathf.Abs(lane) / HordeHalfWidth;
                if (edge > 0.6f && random.NextDouble() < (edge - 0.6f) * 2.2f) continue;
                var cell = new Vector2(Mathf.Floor(x / 7f), Mathf.Floor(lane / 7f));
                var area = Mathf.Abs(Mathf.Sin(Vector2.Dot(cell, new Vector2(12.9898f, 78.233f))) * 43758.5453f) % 1f;
                var status = 0f;
                if (random.NextDouble() < 0.7)
                {
                    if (area < 0.06f) status = 1f;
                    else if (area < 0.09f) status = 2f;
                }

                list.Add(new Walker
                {
                    StartX = x,
                    Lane = lane,
                    Speed = 0.9f + (float)random.NextDouble() * 0.3f,
                    Yaw = ((float)random.NextDouble() - 0.5f) * 16f,
                    Scale = 0.42f + (float)random.NextDouble() * 0.06f,
                    Seed = status + (float)random.NextDouble() * 0.999f
                });
            }

            return list;
        }

        internal static void DrawCrowd(Mesh mesh, Material material, List<Walker> crowd, float time)
        {
            var matrices = new List<Matrix4x4>(crowd.Count);
            var seeds = new List<float>(crowd.Count);
            foreach (var walker in crowd)
            {
                var x = walker.StartX + walker.Speed * time;
                var slope = RoadAmplitude / RoadWavelength * Mathf.Cos(x / RoadWavelength);
                var heading = Mathf.Atan2(1f, slope) * Mathf.Rad2Deg;
                matrices.Add(Matrix(x, RoadCenter(x) + walker.Lane, walker.Scale, heading + walker.Yaw));
                seeds.Add(walker.Seed);
            }

            DrawInstanced(mesh, material, matrices, seeds);
        }

        // Where a walker is on the ground at a given time (same path as DrawCrowd).
        internal static Vector3 WalkerPosition(Walker walker, float time)
        {
            var x = walker.StartX + walker.Speed * time;
            return new Vector3(x, 0f, RoadCenter(x) + walker.Lane);
        }

        private static void DrawStatic(Mesh mesh, Material material, List<Vector3> positions)
        {
            foreach (var position in positions)
            {
                Graphics.DrawMesh(mesh, Matrix4x4.Translate(position), material, 0);
            }
        }

        internal static void DrawInstanced(Mesh mesh, Material material, List<Matrix4x4> matrices, List<float> seeds)
        {
            var block = new MaterialPropertyBlock();
            var batch = new float[1023];
            for (var offset = 0; offset < matrices.Count; offset += 1023)
            {
                var count = Mathf.Min(1023, matrices.Count - offset);
                seeds.CopyTo(offset, batch, 0, count);
                block.SetFloatArray("_Seed", batch);
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices.GetRange(offset, count), block);
            }
        }

        internal static IEnumerator Capture(Camera camera, RenderTexture target, Texture2D texture, string path)
        {
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            yield return null;
        }

        internal static Matrix4x4 Matrix(float x, float z, float scale, float yaw)
        {
            return Matrix4x4.TRS(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
        }

        internal static void PlaceCamera(Camera camera, float height, Vector3 lookAt)
        {
            var groundOffset = height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
            camera.transform.position = lookAt + new Vector3(0f, height, -groundOffset);
            camera.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            camera.orthographicSize = height / Mathf.Sin(Pitch * Mathf.Deg2Rad) *
                Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.aspect = Width / (float)Height;
        }
    }

    /// <summary>
    /// Procedural low-poly meshes. Vertex colour alpha is the animated body part, uv.x marks
    /// skin that takes the per-instance tone (see StyleLowPoly.shader).
    /// </summary>
    internal static class LowPolyMeshes
    {
        private const float Body = 0f;
        private const float LeftLeg = 0.25f;
        private const float RightLeg = 0.5f;
        private const float LeftArm = 0.625f;
        private const float WeaponArm = 0.75f;
        private const float Static = 1f;

        /// <summary>Colours and the optional head mark of a goblin (readability tests).</summary>
        internal struct GoblinLook
        {
            public Color Skin;
            public Color SkinDark;
            public Color Hide;
            public Color Mark;
            public int MarkShape; // 0 none, 1 war-paint stripe over the head, 2 bone cap with horns

            public static GoblinLook Current => new GoblinLook
            {
                Skin = new Color(0.36f, 0.62f, 0.24f),
                SkinDark = new Color(0.26f, 0.47f, 0.17f),
                Hide = new Color(0.47f, 0.31f, 0.18f)
            };
        }

        public static Mesh Goblin() => Goblin(GoblinLook.Current);

        // Prehistoric goblin seen from above (+Z forward): big head with ears and bright eyes,
        // hunched torso, hide loincloth, two legs and a bone-studded club in the right hand.
        public static Mesh Goblin(GoblinLook look)
        {
            var skin = look.Skin;
            var skinDark = look.SkinDark;
            var hide = look.Hide;
            var eye = new Color(1f, 0.8f, 0.22f);
            var dark = new Color(0.1f, 0.13f, 0.07f);
            var bone = new Color(0.92f, 0.88f, 0.72f);
            var wood = new Color(0.4f, 0.25f, 0.13f);
            var builder = new Builder();

            builder.Box(-0.5f, -0.14f, -0.22f, 0.18f, 0f, 0.48f, skinDark, LeftLeg, true);
            builder.Box(-0.54f, -0.1f, -0.18f, 0.36f, 0f, 0.14f, hide, LeftLeg);
            builder.Box(0.14f, 0.5f, -0.22f, 0.18f, 0f, 0.48f, skinDark, RightLeg, true);
            builder.Box(0.1f, 0.54f, -0.18f, 0.36f, 0f, 0.14f, hide, RightLeg);

            // Broad hunched torso; a hide cape over the back gives the top view two tones and
            // shows which way the goblin faces.
            builder.Frustum(Ellipse(0f, -0.05f, 0.86f, 0.56f, 6), 0.42f, 0.98f, 0.86f, skin, Body, true);
            builder.Frustum(Ellipse(0f, -0.05f, 0.9f, 0.62f, 6), 0.38f, 0.6f, 0.96f, hide, Body);
            builder.Frustum(new[] { V(-0.8f, -0.05f), V(-0.62f, -0.5f), V(0f, -0.62f), V(0.62f, -0.5f), V(0.8f, -0.05f), V(0f, 0.05f) },
                0.9f, 1.1f, 0.85f, hide, Body);

            builder.Frustum(Ellipse(0f, 0.26f, 0.5f, 0.48f, 8), 0.95f, 1.24f, 1.06f, skin, Body, true);
            builder.Frustum(Ellipse(0f, 0.26f, 0.53f, 0.51f, 8), 1.24f, 1.44f, 0.6f, skin, Body, true);
            builder.Prism(new[] { V(-0.46f, 0.38f), V(-1.02f, 0.02f), V(-0.46f, 0.12f) }, 1.14f, 1.24f, skinDark, Body, true);
            builder.Prism(new[] { V(0.46f, 0.38f), V(0.46f, 0.12f), V(1.02f, 0.02f) }, 1.14f, 1.24f, skinDark, Body, true);
            builder.Box(-0.36f, 0.36f, 0.5f, 0.58f, 1.34f, 1.46f, dark, Body);
            builder.Box(-0.3f, -0.08f, 0.58f, 0.74f, 1.24f, 1.38f, eye, Body);
            builder.Box(0.08f, 0.3f, 0.58f, 0.74f, 1.24f, 1.38f, eye, Body);
            builder.Box(-0.26f, -0.15f, 0.72f, 0.84f, 0.98f, 1.18f, bone, Body);
            builder.Box(0.15f, 0.26f, 0.72f, 0.84f, 0.98f, 1.18f, bone, Body);

            builder.Box(-1.08f, -0.74f, -0.12f, 0.26f, 0.56f, 0.94f, skin, LeftArm, true);
            builder.Box(0.74f, 1.08f, -0.08f, 0.3f, 0.56f, 0.94f, skin, WeaponArm, true);
            builder.Box(0.84f, 0.98f, 0.2f, 1.02f, 0.68f, 0.82f, wood, WeaponArm);
            builder.Frustum(Ellipse(0.91f, 1.22f, 0.27f, 0.29f, 6), 0.58f, 0.94f, 0.8f, wood, WeaponArm);
            builder.Frustum(Ellipse(0.91f, 1.22f, 0.09f, 0.09f, 4), 0.94f, 1.1f, 0.2f, bone, WeaponArm);
            builder.Prism(new[] { V(1.14f, 1.14f), V(1.32f, 1.22f), V(1.14f, 1.3f) }, 0.7f, 0.82f, bone, WeaponArm);
            builder.Prism(new[] { V(0.68f, 1.14f), V(0.68f, 1.3f), V(0.5f, 1.22f) }, 0.7f, 0.82f, bone, WeaponArm);
            if (look.MarkShape == 1)
            {
                // A broad stripe of paint from the brow back over the crown and down the neck.
                builder.Box(-0.12f, 0.12f, -0.12f, 0.52f, 1.44f, 1.47f, look.Mark, Body);
                builder.Box(-0.2f, 0.2f, 0.36f, 0.5f, 1.44f, 1.475f, look.Mark, Body);
            }
            else if (look.MarkShape == 2)
            {
                // A bone cap on the crown with two short horns pointing back and out.
                builder.Frustum(Ellipse(0f, 0.24f, 0.34f, 0.32f, 7), 1.42f, 1.56f, 0.7f, look.Mark, Body);
                builder.Prism(new[] { V(-0.22f, 0.14f), V(-0.5f, -0.2f), V(-0.12f, 0.04f) }, 1.5f, 1.62f, look.Mark, Body);
                builder.Prism(new[] { V(0.22f, 0.14f), V(0.12f, 0.04f), V(0.5f, -0.2f) }, 1.5f, 1.62f, look.Mark, Body);
            }

            return builder.ToMesh("LowPolyGoblin");
        }

        // Prehistoric watchtower in metres. The camera sees it from above, so the read comes from
        // the top: a plank platform ringed by stakes, a small thatched canopy off-centre, a
        // banner in the player colour and a stone footing that shows around the platform.
        public static Mesh Tower()
        {
            var stone = new Color(0.62f, 0.58f, 0.52f);
            var wood = new Color(0.42f, 0.27f, 0.15f);
            var plank = new Color(0.66f, 0.47f, 0.28f);
            var thatch = new Color(0.88f, 0.69f, 0.34f);
            var banner = new Color(0.22f, 0.5f, 0.95f);
            var builder = new Builder();
            builder.Frustum(Jagged(0f, 0f, 2.3f, 9, 0.85f, 3), 0f, 0.8f, 0.85f, stone, Static);
            for (var i = 0; i < 4; i++)
            {
                var angle = (45f + i * 90f) * Mathf.Deg2Rad;
                var x = Mathf.Cos(angle) * 1.1f;
                var z = Mathf.Sin(angle) * 1.1f;
                builder.Box(x - 0.17f, x + 0.17f, z - 0.17f, z + 0.17f, 0.8f, 3.5f, wood, Static);
            }

            builder.Prism(Ellipse(0f, 0f, 1.7f, 1.7f, 8), 3.3f, 3.55f, plank, Static);
            for (var i = 0; i < 4; i++)
            {
                // Dark seams between the planks.
                var x = -1.2f + i * 0.8f;
                builder.Box(x - 0.03f, x + 0.03f, -1.45f, 1.45f, 3.55f, 3.57f, wood, Static);
            }

            for (var i = 0; i < 14; i++)
            {
                var angle = i * Mathf.PI * 2f / 14f;
                builder.Frustum(
                    Ellipse(Mathf.Cos(angle) * 1.58f, Mathf.Sin(angle) * 1.58f, 0.13f, 0.13f, 5), 3.55f, 4.3f, 0.12f, wood, Static);
            }

            builder.Box(-0.55f, -0.43f, 0.15f, 0.27f, 3.55f, 4.6f, wood, Static);
            builder.Frustum(Ellipse(-0.5f, 0.2f, 1.0f, 1.0f, 7), 4.6f, 5.5f, 0.08f, thatch, Static);
            builder.Box(0.9f, 1.02f, -0.95f, -0.83f, 3.55f, 6.4f, wood, Static);
            builder.Prism(new[] { V(1.02f, -0.94f), V(2.4f, -0.78f), V(2.1f, -0.5f), V(2.4f, -0.22f), V(1.02f, -0.3f) },
                5.6f, 6.3f, banner, Static);
            return builder.ToMesh("LowPolyTower");
        }

        // Plateaus with an optional second terrace on both sides of the road, plus boulders on the
        // road margins. Built in world space as one static mesh.
        public static Mesh Terrain(System.Func<float, float> roadCenter, List<Vector3> towers)
        {
            // Sandstone in the ground's own hue family, lighter on each terrace.
            var stoneTop = new Color(0.58f, 0.45f, 0.34f);
            var stoneHigh = new Color(0.66f, 0.53f, 0.4f);
            var rock = new Color(0.5f, 0.4f, 0.31f);
            var random = new System.Random(5);
            var builder = new Builder();
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var (offset, minRadius, maxRadius, minStep, maxStep) in new[]
                {
                    (21f, 12f, 24f, 30f, 48f), (75f, 18f, 36f, 55f, 90f), (150f, 25f, 50f, 70f, 120f)
                })
                {
                    for (var x = -560f + Range(0f, 30f); x < 560f; x += Range(minStep, maxStep))
                    {
                        var radius = Range(minRadius, maxRadius);
                        var center = new Vector2(x, roadCenter(x) + side * (offset + radius * 0.85f));
                        var top = Range(4f, 6f);
                        builder.Frustum(Jagged(center.x, center.y, radius, 11, 0.72f, random.Next()), 0f, top, 0.9f, stoneTop, Static);
                        if (random.NextDouble() < 0.6)
                        {
                            var inner = new Vector2(center.x + Range(-0.15f, 0.15f) * radius, center.y + Range(-0.15f, 0.15f) * radius);
                            builder.Frustum(
                                Jagged(inner.x, inner.y, radius * Range(0.45f, 0.62f), 9, 0.72f, random.Next()),
                                top, top + Range(2.5f, 4f), 0.88f, stoneHigh, Static);
                        }
                    }
                }
            }

            for (var i = 0; i < 260; i++)
            {
                var x = Range(-520f, 520f);
                var lane = (random.NextDouble() < 0.5 ? -1f : 1f) * Range(HordeEdge, 22f);
                var position = new Vector2(x, roadCenter(x) + lane);
                var clear = true;
                foreach (var tower in towers)
                {
                    if (Vector2.Distance(position, new Vector2(tower.x, tower.z)) < 3f) clear = false;
                }

                if (!clear) continue;
                var size = Range(0.35f, 1.4f);
                builder.Frustum(Jagged(position.x, position.y, size, 6, 0.7f, random.Next()), 0f, size * Range(0.6f, 1.0f),
                    Range(0.45f, 0.7f), rock, Static);
            }

            return builder.ToMesh("LowPolyTerrain");
        }

        private const float HordeEdge = 13.5f;

        private static Vector2 V(float x, float z) => new Vector2(x, z);

        private static Vector2[] Ellipse(float cx, float cz, float rx, float rz, int segments)
        {
            var points = new Vector2[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = (i + 0.5f) * Mathf.PI * 2f / segments;
                points[i] = new Vector2(cx + Mathf.Cos(angle) * rx, cz + Mathf.Sin(angle) * rz);
            }

            return points;
        }

        // Irregular outline: radius jitters between minFraction and 1, angles jitter slightly.
        private static Vector2[] Jagged(float cx, float cz, float radius, int segments, float minFraction, int seed)
        {
            var random = new System.Random(seed);
            var points = new Vector2[segments];
            var turn = (float)random.NextDouble();
            for (var i = 0; i < segments; i++)
            {
                var angle = (i + turn + ((float)random.NextDouble() - 0.5f) * 0.5f) * Mathf.PI * 2f / segments;
                var r = radius * (minFraction + (float)random.NextDouble() * (1f - minFraction));
                points[i] = new Vector2(cx + Mathf.Cos(angle) * r, cz + Mathf.Sin(angle) * r);
            }

            return points;
        }

        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color> colors = new List<Color>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            public void Box(float x0, float x1, float z0, float z1, float bottom, float top, Color color, float part, bool skin = false)
            {
                Prism(new[] { V(x0, z0), V(x1, z0), V(x1, z1), V(x0, z1) }, bottom, top, color, part, skin);
            }

            public void Prism(Vector2[] outline, float bottom, float top, Color color, float part, bool skin = false)
            {
                Frustum(outline, bottom, top, 1f, color, part, skin);
            }

            // Extrudes a star-shaped outline between two heights; the top is scaled about the
            // centroid, so sides slope inward and catch a different light band.
            public void Frustum(Vector2[] outline, float bottom, float top, float topScale, Color color, float part, bool skin = false)
            {
                var area = 0f;
                for (var i = 0; i < outline.Length; i++)
                {
                    var next = outline[(i + 1) % outline.Length];
                    area += outline[i].x * next.y - next.x * outline[i].y;
                }

                var points = (Vector2[])outline.Clone();
                if (area < 0f) System.Array.Reverse(points);

                var center = Vector2.zero;
                foreach (var point in points) center += point;
                center /= points.Length;
                var tops = new Vector2[points.Length];
                for (var i = 0; i < points.Length; i++) tops[i] = center + (points[i] - center) * topScale;

                var first = vertices.Count;
                vertices.Add(new Vector3(center.x, top, center.y));
                normals.Add(Vector3.up);
                foreach (var point in tops)
                {
                    vertices.Add(new Vector3(point.x, top, point.y));
                    normals.Add(Vector3.up);
                }

                for (var i = 0; i < points.Length; i++)
                {
                    // Seen from above (looking down -Y) counter-clockwise XZ is clockwise on screen.
                    triangles.Add(first);
                    triangles.Add(first + 1 + (i + 1) % points.Length);
                    triangles.Add(first + 1 + i);
                }

                for (var i = 0; i < points.Length; i++)
                {
                    var j = (i + 1) % points.Length;
                    var a = new Vector3(points[i].x, bottom, points[i].y);
                    var b = new Vector3(points[j].x, bottom, points[j].y);
                    var bTop = new Vector3(tops[j].x, top, tops[j].y);
                    var aTop = new Vector3(tops[i].x, top, tops[i].y);
                    var normal = Vector3.Cross(aTop - a, b - a).normalized;
                    var start = vertices.Count;
                    vertices.Add(a);
                    vertices.Add(b);
                    vertices.Add(bTop);
                    vertices.Add(aTop);
                    for (var k = 0; k < 4; k++) normals.Add(normal);
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
                }

                var tint = new Color(color.r, color.g, color.b, part);
                var uv = new Vector2(skin ? 1f : 0f, 0f);
                while (colors.Count < vertices.Count)
                {
                    colors.Add(tint);
                    uvs.Add(uv);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
