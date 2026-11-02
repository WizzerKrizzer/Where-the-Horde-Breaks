using System;
using System.Collections.Generic;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Interactive, test-only viewer for CrowdLabRunner. It intentionally uses
    /// plain geometry so sprite size, animation and shadows cannot hide contact
    /// behavior. Open Assets/Scenes/CrowdLab.unity and press Play.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera), typeof(CrowdLabRunner))]
    public sealed class CrowdLabPreview : MonoBehaviour
    {
        private const float BodyDiameter = 0.43f;
        private const float BodyRadius = BodyDiameter * 0.5f;
        private const int DiskSegments = 16;
        private const int MaximumRenderedBodies = 10000;
        private const int MaximumContactBodies = 12000;
        private const int MaximumFlowArrows = 96;
        private const int MaximumTrailBodies = 192;
        private const int MaximumTrailFrames = 48;

        private static readonly string[] ScenarioLabels =
        {
            "Two Bodies", "Queue Release", "Wall Impact",
            "Right-Angle Bend", "Three-Way Merge", "Blocked Exit",
            "Explosion Recovery", "10,000 Stress", "50,000 Stress"
        };

        [SerializeField] private CrowdLabScenario scenario = CrowdLabScenario.WallImpact;
        [SerializeField] private int seed = 1701;
        [SerializeField, Range(0.1f, 4f)] private float simulationSpeed = 0.25f;
        [SerializeField] private bool startPaused = true;
        [SerializeField] private bool showContactOutlines = true;
        [SerializeField] private bool showFlowVectors = true;
        [SerializeField] private bool showTrails = true;

        private readonly List<HordeFlowField.BoundarySegment> boundaries = new();
        private readonly List<Vector3> walkableCells = new();
        private readonly List<Vector2[]> trailFrames = new();
        private readonly Dictionary<Vector2Int, List<int>> contactCells = new();
        private readonly Stack<List<int>> contactCellPool = new();

        private Camera previewCamera;
        private CrowdLabRunner runner;
        private CrowdLabReport latestReport;
        private GpuHordeSimulation.AgentState[] states = Array.Empty<GpuHordeSimulation.AgentState>();
        private Material lineMaterial;
        private byte[] contactKinds = Array.Empty<byte>();
        private int[] trailIndices = Array.Empty<int>();
        private bool running;
        private float tickAccumulator;
        private string previewError;
        private GUIStyle titleStyle;
        private GUIStyle diagnosticStyle;

        public CrowdLabScenario Scenario => scenario;
        public bool IsRunning => running;

        private void Awake()
        {
            previewCamera = GetComponent<Camera>();
            runner = GetComponent<CrowdLabRunner>();
            runner.SetGizmoTrails(false);
            ConfigureCamera();
            ResetScenario();
            running = !startPaused;
        }

        private void Update()
        {
            HandleKeyboard();
            HandleCameraNavigation();
            if (!running || !string.IsNullOrEmpty(previewError)) return;

            tickAccumulator += Time.unscaledDeltaTime * simulationSpeed;
            var ticksThisFrame = 0;
            while (tickAccumulator >= runner.FixedStep && ticksThisFrame < 8)
            {
                StepOneTick();
                tickAccumulator -= runner.FixedStep;
                ticksThisFrame++;
            }
        }

        public void ResetScenario()
        {
            previewError = null;
            latestReport = null;
            tickAccumulator = 0f;
            trailFrames.Clear();
            try
            {
                runner.Configure(scenario, seed);
                states = runner.LatestStates ?? Array.Empty<GpuHordeSimulation.AgentState>();
                CacheFlowGeometry();
                BuildTrailSelection();
                RefreshContactOutlines();
                CaptureTrailFrame();
                FitCameraToScenario();
            }
            catch (Exception exception)
            {
                running = false;
                previewError = $"{exception.GetType().Name}: {exception.Message}";
                Debug.LogException(exception, this);
            }
        }

        public void StepOneTick()
        {
            if (!string.IsNullOrEmpty(previewError)) return;
            try
            {
                latestReport = runner.RunFixedTicks(1, presentationBatch: 1, writeReport: false);
                states = runner.LatestStates ?? Array.Empty<GpuHordeSimulation.AgentState>();
                RefreshContactOutlines();
                CaptureTrailFrame();
            }
            catch (Exception exception)
            {
                running = false;
                previewError = $"{exception.GetType().Name}: {exception.Message}";
                Debug.LogException(exception, this);
            }
        }

        private void HandleKeyboard()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) running = !running;
            if (UnityEngine.Input.GetKeyDown(KeyCode.N))
            {
                running = false;
                StepOneTick();
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) ResetScenario();
        }

        private void HandleCameraNavigation()
        {
            if (previewCamera == null) return;
            var wheel = UnityEngine.Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                previewCamera.orthographicSize = Mathf.Clamp(
                    previewCamera.orthographicSize * Mathf.Pow(0.88f, wheel), 1.5f, 260f);
            }

            if (!UnityEngine.Input.GetMouseButton(1)) return;
            var delta = new Vector2(UnityEngine.Input.GetAxisRaw("Mouse X"),
                UnityEngine.Input.GetAxisRaw("Mouse Y"));
            var scale = previewCamera.orthographicSize * 0.018f;
            previewCamera.transform.position += new Vector3(-delta.x * scale, 0f, -delta.y * scale);
        }

        private void ConfigureCamera()
        {
            previewCamera.orthographic = true;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = PlaceholderPalette.Grey(.023f, 1f);
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 600f;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void CacheFlowGeometry()
        {
            boundaries.Clear();
            walkableCells.Clear();
            var field = runner.FlowField;
            if (field == null) return;
            field.GetBoundarySegments(boundaries);
            for (var y = 0; y < field.Height; y++)
            {
                for (var x = 0; x < field.Width; x++)
                {
                    var center = field.Origin + new Vector3(
                        (x + 0.5f) * field.CellSize, 0f, (y + 0.5f) * field.CellSize);
                    if (field.IsWalkable(center)) walkableCells.Add(center);
                }
            }
        }

        private void BuildTrailSelection()
        {
            var count = Mathf.Min(MaximumTrailBodies, states.Length);
            trailIndices = new int[count];
            if (count == 0) return;
            var stride = Mathf.Max(1, states.Length / count);
            for (var i = 0; i < count; i++) trailIndices[i] = Mathf.Min(states.Length - 1, i * stride);
        }

        private void CaptureTrailFrame()
        {
            if (!showTrails || trailIndices.Length == 0) return;
            var frame = new Vector2[trailIndices.Length];
            for (var i = 0; i < trailIndices.Length; i++)
            {
                var position = states[trailIndices[i]].Position;
                frame[i] = position;
            }
            trailFrames.Add(frame);
            if (trailFrames.Count > MaximumTrailFrames) trailFrames.RemoveAt(0);
        }

        private void RefreshContactOutlines()
        {
            if (contactKinds.Length != states.Length) contactKinds = new byte[states.Length];
            else Array.Clear(contactKinds, 0, contactKinds.Length);
            if (!showContactOutlines || states.Length > MaximumContactBodies) return;

            foreach (var entry in contactCells)
            {
                entry.Value.Clear();
                contactCellPool.Push(entry.Value);
            }
            contactCells.Clear();
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status != 1u) continue;
                var position = states[i].Position;
                var cell = new Vector2Int(
                    Mathf.FloorToInt(position.x / BodyDiameter),
                    Mathf.FloorToInt(position.y / BodyDiameter));
                if (!contactCells.TryGetValue(cell, out var indices))
                {
                    indices = contactCellPool.Count > 0 ? contactCellPool.Pop() : new List<int>(8);
                    contactCells.Add(cell, indices);
                }
                indices.Add(i);
            }

            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status != 1u) continue;
                var position = states[i].Position;
                var center = new Vector2Int(
                    Mathf.FloorToInt(position.x / BodyDiameter),
                    Mathf.FloorToInt(position.y / BodyDiameter));
                for (var y = -1; y <= 1; y++)
                {
                    for (var x = -1; x <= 1; x++)
                    {
                        if (!contactCells.TryGetValue(center + new Vector2Int(x, y), out var neighbors)) continue;
                        for (var slot = 0; slot < neighbors.Count; slot++)
                        {
                            var other = neighbors[slot];
                            if (other <= i) continue;
                            var distance = Vector2.Distance(position, states[other].Position);
                            if (distance > BodyDiameter * 1.05f) continue;
                            var kind = distance < BodyDiameter * 0.95f ? (byte)2 : (byte)1;
                            contactKinds[i] = Math.Max(contactKinds[i], kind);
                            contactKinds[other] = Math.Max(contactKinds[other], kind);
                        }
                    }
                }

                var world = new Vector3(position.x, 0f, position.y);
                if (runner.FlowField.GetWallRepulsion(world, BodyRadius * 1.05f).sqrMagnitude > 0.0001f)
                {
                    contactKinds[i] = Math.Max(contactKinds[i], (byte)1);
                }
            }
        }

        private void FitCameraToScenario()
        {
            var field = runner.FlowField;
            if (field == null) return;
            var worldWidth = field.Width * field.CellSize;
            var worldHeight = field.Height * field.CellSize;
            var center = field.Origin + new Vector3(worldWidth * 0.5f, 0f, worldHeight * 0.5f);
            previewCamera.transform.position = new Vector3(center.x, 100f, center.z);
            var aspect = Mathf.Max(0.25f, previewCamera.aspect);
            previewCamera.orthographicSize = Mathf.Max(worldHeight * 0.55f, worldWidth * 0.55f / aspect) + 1f;
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            var width = Mathf.Min(430f, Screen.width - 24f);
            var height = Mathf.Min(680f, Screen.height - 24f);
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), GUI.skin.box);
            GUILayout.Label("XPBD CROWD LAB", titleStyle);
            GUILayout.Label("Plain disks show physical bodies, independent of sprite artwork.");

            var selected = GUILayout.SelectionGrid((int)scenario, ScenarioLabels, 3,
                GUILayout.Height(78f));
            if (selected != (int)scenario)
            {
                scenario = (CrowdLabScenario)selected;
                seed = DefaultSeed(scenario);
                running = false;
                ResetScenario();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(running ? "PAUSE" : "PLAY", GUILayout.Height(32f))) running = !running;
            if (GUILayout.Button("STEP 1 TICK", GUILayout.Height(32f)))
            {
                running = false;
                StepOneTick();
            }
            if (GUILayout.Button("RESET", GUILayout.Height(32f))) ResetScenario();
            GUILayout.EndHorizontal();

            GUILayout.Label($"Speed: {simulationSpeed:F2}x  (slow motion is 0.10x)");
            simulationSpeed = GUILayout.HorizontalSlider(simulationSpeed, 0.1f, 4f);

            GUILayout.BeginHorizontal();
            var contactsBefore = showContactOutlines;
            showContactOutlines = GUILayout.Toggle(showContactOutlines, "Contacts");
            if (showContactOutlines && !contactsBefore) RefreshContactOutlines();
            showFlowVectors = GUILayout.Toggle(showFlowVectors, "Flow vectors");
            var trailsBefore = showTrails;
            showTrails = GUILayout.Toggle(showTrails, "Trails");
            if (showTrails && !trailsBefore) CaptureTrailFrame();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("FIT CAMERA")) FitCameraToScenario();
            GUILayout.Space(4f);
            if (!string.IsNullOrEmpty(previewError))
            {
                var previous = GUI.color;
                GUI.color = PlaceholderPalette.Grey(.585f);
                GUILayout.Label(previewError, diagnosticStyle);
                GUI.color = previous;
            }
            else
            {
                GUILayout.Label(BuildDiagnosticText(), diagnosticStyle);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Space: play/pause   N: one tick   R: reset\n" +
                            "Mouse wheel: zoom   Right-drag: pan");
            GUILayout.EndArea();
        }

        private string BuildDiagnosticText()
        {
            var active = 0;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status == 1u) active++;
            }

            if (latestReport == null)
            {
                return $"Seed {seed}   Tick {runner.CurrentTick}\n" +
                       $"Bodies {states.Length}   Active {active}\n" +
                       "Press Play or Step 1 Tick to begin.";
            }

            return $"Seed {seed}   Tick {runner.CurrentTick}   dt {runner.FixedStep:F4}s\n" +
                   $"Bodies {states.Length}   Active {active}\n" +
                   $"Shallow {latestReport.ShallowContactPairs}   Deep {latestReport.DeepOverlapPairs}\n" +
                   $"Persistent deep bodies {latestReport.PersistentDeepContactBodies}\n" +
                   $"Constraints {latestReport.ActiveConstraints}   Max cell {latestReport.MaximumCellOccupancy}\n" +
                   $"Grid/pair failures {latestReport.GridCapacityFailures}\n" +
                   $"Walkability violations {latestReport.WalkabilityViolations}\n" +
                   $"Forward throughput {latestReport.ForwardThroughput:F2}   Backward {latestReport.BackwardBodies}";
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = PlaceholderPalette.Grey(.738f) }
            };
            diagnosticStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = PlaceholderPalette.Grey(.89f) }
            };
        }

        private void OnPostRender()
        {
            if (states == null || runner == null || runner.FlowField == null) return;
            if (!EnsureLineMaterial()) return;

            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(previewCamera.projectionMatrix);
            GL.modelview = previewCamera.worldToCameraMatrix;
            DrawWalkableSurface();
            if (showTrails) DrawTrails();
            DrawBodies();
            if (showFlowVectors) DrawFlowVectors();
            DrawBoundaries();
            DrawBlocker();
            GL.PopMatrix();
        }

        private bool EnsureLineMaterial()
        {
            if (lineMaterial != null) return true;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                previewError ??= "Unity's Hidden/Internal-Colored shader is unavailable.";
                return false;
            }
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return true;
        }

        private void DrawWalkableSurface()
        {
            var half = runner.FlowField.CellSize * 0.51f;
            GL.Begin(GL.QUADS);
            GL.Color(PlaceholderPalette.Grey(.214f, 1f));
            foreach (var center in walkableCells)
            {
                GL.Vertex3(center.x - half, -0.04f, center.z - half);
                GL.Vertex3(center.x - half, -0.04f, center.z + half);
                GL.Vertex3(center.x + half, -0.04f, center.z + half);
                GL.Vertex3(center.x + half, -0.04f, center.z - half);
            }
            GL.End();
        }

        private void DrawBodies()
        {
            var stride = Mathf.Max(1, Mathf.CeilToInt(states.Length / (float)MaximumRenderedBodies));
            for (var i = 0; i < states.Length; i += stride)
            {
                if (states[i].Status != 1u) continue;
                var position = states[i].Position;
                DrawDisk(position, BodyRadius, PlaceholderPalette.Grey(.548f, 0.92f), 0f);
                if (!showContactOutlines || contactKinds.Length <= i || contactKinds[i] == 0) continue;
                var outline = contactKinds[i] >= 2
                    ? PlaceholderPalette.Grey(.418f, 1f)
                    : PlaceholderPalette.Grey(.771f, 1f);
                DrawCircle(position, BodyRadius * 1.08f, outline, 0.02f);
            }
        }

        private void DrawBoundaries()
        {
            GL.Begin(GL.LINES);
            GL.Color(PlaceholderPalette.Grey(.785f, 0.95f));
            foreach (var segment in boundaries)
            {
                GL.Vertex3(segment.From.x, 0.035f, segment.From.z);
                GL.Vertex3(segment.To.x, 0.035f, segment.To.z);
            }
            GL.End();
        }

        private void DrawFlowVectors()
        {
            if (states.Length == 0) return;
            var stride = Mathf.Max(1, states.Length / MaximumFlowArrows);
            GL.Begin(GL.LINES);
            GL.Color(PlaceholderPalette.Grey(.725f, 0.82f));
            for (var i = 0; i < states.Length; i += stride)
            {
                if (states[i].Status != 1u) continue;
                var p = states[i].Position;
                var world = new Vector3(p.x, 0f, p.y);
                var direction3 = runner.FlowField.GetDirection(world);
                var direction = new Vector2(direction3.x, direction3.z).normalized;
                var tip = p + direction * 0.55f;
                var side = new Vector2(-direction.y, direction.x) * 0.12f;
                GL.Vertex3(p.x, 0.045f, p.y);
                GL.Vertex3(tip.x, 0.045f, tip.y);
                GL.Vertex3(tip.x, 0.045f, tip.y);
                GL.Vertex3(tip.x - direction.x * 0.16f + side.x, 0.045f,
                    tip.y - direction.y * 0.16f + side.y);
                GL.Vertex3(tip.x, 0.045f, tip.y);
                GL.Vertex3(tip.x - direction.x * 0.16f - side.x, 0.045f,
                    tip.y - direction.y * 0.16f - side.y);
            }
            GL.End();
        }

        private void DrawTrails()
        {
            if (trailFrames.Count < 2) return;
            GL.Begin(GL.LINES);
            GL.Color(PlaceholderPalette.Grey(.594f, 0.42f));
            for (var frame = 1; frame < trailFrames.Count; frame++)
            {
                var from = trailFrames[frame - 1];
                var to = trailFrames[frame];
                for (var body = 0; body < to.Length; body++)
                {
                    GL.Vertex3(from[body].x, 0.01f, from[body].y);
                    GL.Vertex3(to[body].x, 0.01f, to[body].y);
                }
            }
            GL.End();
        }

        private void DrawBlocker()
        {
            var blocker = runner.ActiveBlocker;
            if (blocker == null) return;
            var position = new Vector2(blocker.Position.x, blocker.Position.z);
            DrawDisk(position, blocker.CombatRadius, PlaceholderPalette.Grey(.43f, 0.72f), 0.01f);
            DrawCircle(position, blocker.CombatRadius, PlaceholderPalette.Grey(.677f, 1f), 0.04f);
        }

        private static void DrawDisk(Vector2 center, float radius, Color color, float height)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (var segment = 0; segment < DiskSegments; segment++)
            {
                var first = segment * Mathf.PI * 2f / DiskSegments;
                var second = (segment + 1) * Mathf.PI * 2f / DiskSegments;
                GL.Vertex3(center.x, height, center.y);
                GL.Vertex3(center.x + Mathf.Cos(first) * radius, height, center.y + Mathf.Sin(first) * radius);
                GL.Vertex3(center.x + Mathf.Cos(second) * radius, height, center.y + Mathf.Sin(second) * radius);
            }
            GL.End();
        }

        private static void DrawCircle(Vector2 center, float radius, Color color, float height)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var segment = 0; segment < DiskSegments; segment++)
            {
                var first = segment * Mathf.PI * 2f / DiskSegments;
                var second = (segment + 1) * Mathf.PI * 2f / DiskSegments;
                GL.Vertex3(center.x + Mathf.Cos(first) * radius, height, center.y + Mathf.Sin(first) * radius);
                GL.Vertex3(center.x + Mathf.Cos(second) * radius, height, center.y + Mathf.Sin(second) * radius);
            }
            GL.End();
        }

        private static int DefaultSeed(CrowdLabScenario selected) => selected switch
        {
            CrowdLabScenario.TwoBodies => 9,
            CrowdLabScenario.QueueRelease => 411,
            CrowdLabScenario.WallImpact => 1701,
            CrowdLabScenario.RightAngleBend => 1451,
            CrowdLabScenario.ThreeWayMerge => 761905,
            CrowdLabScenario.BlockedExit => 8201,
            CrowdLabScenario.ExplosionRecovery => 9101,
            CrowdLabScenario.StressTenThousand => 10001,
            CrowdLabScenario.StressFiftyThousand => 50001,
            _ => 761905
        };

        private void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
