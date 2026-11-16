using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The fire on the burning rock (user, 2026-11-12): tongues of flame keep leaping off the rock and
    /// rise, flicker and die where they were left, so a rolling rock trails a stream of fire; a small
    /// flame that never stops changing size clings to its top, sparks fly up behind it and the ground
    /// under it glows orange. When the rock bursts, the fire bursts with it. It follows the ball
    /// without turning with it. Sizes TEMPORARY.
    /// </summary>
    public sealed class RockFireVisual : MonoBehaviour
    {
        private static readonly int LeanId = Shader.PropertyToID("_Lean");
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly Color SparkColor = new(1f, 0.78f, 0.3f, 1f);
        private static Material glowMaterial;

        // The flames clinging to the top: offset (across, up, back) and height, in ball radii.
        private static readonly (Vector3 Offset, float Height)[] Core =
        {
            (new Vector3(0f, 0.5f, 0.1f), 1.5f),
            (new Vector3(0.35f, 0.35f, 0.3f), 1.0f),
            (new Vector3(-0.35f, 0.35f, 0.25f), 1.1f)
        };

        // Metres of bend per metre of flame for each m/s, and the most it bends.
        private const float LeanPerSpeed = 0.08f;
        private const float MaxLean = 0.9f;

        // Tongues: how many at once, how many a second and how long each burns.
        private const int TongueCount = 40;
        private const float TonguesPerSecond = 46f;
        private const float TongueMinSeconds = 0.32f;
        private const float TongueMaxSeconds = 0.6f;
        private const int SparkCount = 24;
        private const float SparkSeconds = 0.7f;
        private const float SparksPerSecond = 16f;

        private struct Particle
        {
            public Transform Transform;
            public Renderer[] Renderers;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Life;
            public float Size;
        }

        private Transform ball;
        private float radius;
        private float fullScale;
        private Transform[] core;
        private Renderer[][] coreRenderers;
        private float[] corePhase;
        private Transform glow;
        private Particle[] tongues;
        private int nextTongue;
        private float tongueTimer;
        private Particle[] sparks;
        private int nextSpark;
        private float sparkTimer;
        private Vector3 lastPosition;
        private Vector3 velocity;
        private Vector3 lean;
        private bool burst;
        private MaterialPropertyBlock block;

        public static int BurstsShown { get; private set; }

        /// <summary>Lights the ball (a child of `parent`, `ballRadius` metres) on fire.</summary>
        public static RockFireVisual Attach(Transform parent, Transform ball, float ballRadius)
        {
            var root = new GameObject("RockFire");
            root.transform.SetParent(parent, false);
            var fire = root.AddComponent<RockFireVisual>();
            fire.Build(ball, ballRadius);
            return fire;
        }

        /// <summary>The rock bursts: a last ring of flames and sparks thrown out, then no more fire.</summary>
        public void Burst()
        {
            if (burst || ball == null)
            {
                return;
            }

            burst = true;
            BurstsShown++;
            var center = ball.position;
            for (var i = 0; i < 16; i++)
            {
                var outward = Random.insideUnitCircle.normalized;
                var direction = new Vector3(outward.x, 0f, outward.y);
                EmitTongue(center + direction * radius * 0.5f,
                    direction * Random.Range(1.5f, 3f) + Vector3.up * Random.Range(1f, 2.2f), 1.25f);
            }

            for (var i = 0; i < 14; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) + 0.4f;
                EmitSpark(center, direction.normalized * Random.Range(3f, 5.5f));
            }
        }

        private void Build(Transform target, float ballRadius)
        {
            ball = target;
            radius = ballRadius;
            fullScale = Mathf.Max(1e-4f, ball.localScale.x);
            transform.position = ball.position;
            lastPosition = ball.position;
            block = new MaterialPropertyBlock();
            core = new Transform[Core.Length];
            coreRenderers = new Renderer[Core.Length][];
            corePhase = new float[Core.Length];
            for (var i = 0; i < Core.Length; i++)
            {
                var flame = FlameVisual.Attach(transform, Core[i].Offset * radius, 1f, 0.5f);
                core[i] = flame.transform;
                coreRenderers[i] = flame.GetComponentsInChildren<Renderer>();
                corePhase[i] = Random.Range(0f, 100f);
            }

            var glowShader = Shader.Find("TowerDefense/StyleGlow");
            if (glowShader != null)
            {
                glowMaterial ??= new Material(glowShader) { name = "RockGlow" };
                var plate = new GameObject("GroundGlow");
                plate.transform.SetParent(transform, false);
                plate.AddComponent<MeshFilter>().sharedMesh = LowPolyShapes.Disc;
                var renderer = plate.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = glowMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                glow = plate.transform;
            }

            // Tongues and sparks live on the rock's own root, which stays put, so each is left behind
            // where it leapt off.
            tongues = new Particle[TongueCount];
            for (var i = 0; i < TongueCount; i++)
            {
                var flame = FlameVisual.Attach(transform.parent, Vector3.zero, 1f, 0.55f);
                flame.name = "Tongue";
                flame.gameObject.SetActive(false);
                tongues[i] = new Particle { Transform = flame.transform, Renderers = flame.GetComponentsInChildren<Renderer>(), Age = 1f, Life = 1f };
            }

            sparks = new Particle[SparkCount];
            for (var i = 0; i < SparkCount; i++)
            {
                // A little glowing gem that flies along its streak.
                var spark = new GameObject("Spark");
                spark.transform.SetParent(transform.parent, false);
                spark.AddComponent<MeshFilter>().sharedMesh = LowPolyShapes.Gem;
                var sparkRenderer = spark.AddComponent<MeshRenderer>();
                sparkRenderer.sharedMaterial = BootstrapMaterials.GetUnlit(SparkColor);
                sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spark.SetActive(false);
                sparks[i] = new Particle { Transform = spark.transform, Age = SparkSeconds, Life = SparkSeconds };
            }
        }

        private void LateUpdate()
        {
            if (ball == null)
            {
                return;
            }

            var dt = Mathf.Max(1e-4f, Time.deltaTime);
            var position = ball.position;
            var moved = (position - lastPosition) / dt;
            lastPosition = position;
            velocity = Vector3.Lerp(velocity, moved, 1f - Mathf.Exp(-dt * 8f));
            var ground = new Vector3(velocity.x, 0f, velocity.z);
            var wanted = Vector3.ClampMagnitude(-ground * LeanPerSpeed, MaxLean);
            lean = Vector3.Lerp(lean, wanted, 1f - Mathf.Exp(-dt * 6f));

            // The rock fades by shrinking (or is gone at once when it bursts); the fire goes with it.
            var size = burst ? 0f : Mathf.Clamp01(ball.localScale.x / fullScale);
            transform.position = position;
            transform.localScale = Vector3.one * size;
            UpdateCore(ground);
            if (glow != null)
            {
                // Flat on the ground under the rock, faint while it is still high in the air, and
                // breathing with the flames.
                var flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 6f, 0.3f);
                glow.SetPositionAndRotation(new Vector3(position.x, 0.03f, position.z), Quaternion.identity);
                glow.localScale = Vector3.one * radius * 6.5f * flicker * Mathf.Clamp01(1.2f - (position.y - radius) / 6f);
            }

            if (!burst && size > 0.3f)
            {
                tongueTimer += dt * TonguesPerSecond;
                while (tongueTimer >= 1f)
                {
                    tongueTimer -= 1f;
                    // Off the upper part of the rock, more off its back.
                    var direction = Random.onUnitSphere;
                    direction.y = Mathf.Abs(direction.y) * 0.8f + 0.2f;
                    if (ground.sqrMagnitude > 0.01f && Vector3.Dot(direction, ground) > 0f && Random.value < 0.6f)
                    {
                        direction -= 2f * Vector3.Project(new Vector3(direction.x, 0f, direction.z), ground.normalized);
                    }

                    var start = position + direction.normalized * radius * 0.8f * size;
                    var rise = Vector3.up * Random.Range(1.6f, 2.8f) + Random.insideUnitSphere * 0.5f + ground * 0.12f;
                    EmitTongue(start, rise, size);
                }

                sparkTimer += dt * SparksPerSecond * (0.5f + Mathf.Clamp01(ground.magnitude / 6f));
                while (sparkTimer >= 1f)
                {
                    sparkTimer -= 1f;
                    var start = position + Random.insideUnitSphere * radius * 0.7f;
                    start.y = position.y + radius * Random.Range(0.2f, 0.9f);
                    EmitSpark(start, new Vector3(Random.Range(-1f, 1f), Random.Range(2.2f, 3.6f), Random.Range(-1f, 1f)) - ground * 0.35f);
                }
            }

            UpdateTongues(dt);
            UpdateSparks(dt);
        }

        // The flames on the rock never hold still: each grows and sinks on its own beat.
        private void UpdateCore(Vector3 ground)
        {
            var back = ground.sqrMagnitude > 0.01f ? -ground.normalized : Vector3.zero;
            var side = Vector3.Cross(Vector3.up, back);
            var t = Time.time;
            for (var i = 0; i < core.Length; i++)
            {
                var (offset, height) = Core[i];
                var placed = offset * radius;
                if (back.sqrMagnitude > 0f)
                {
                    placed = side * placed.x + Vector3.up * placed.y + back * placed.z;
                }

                core[i].localPosition = placed;
                var beat = Mathf.PerlinNoise(t * 4.5f, corePhase[i]);
                var quick = Mathf.PerlinNoise(t * 11f, corePhase[i] + 7f);
                var flameHeight = height * radius * (0.55f + 0.6f * beat + 0.25f * quick);
                core[i].localScale = new Vector3(0.75f + 0.5f * quick, 1f, 1f) * flameHeight;
                SetBlock(coreRenderers[i], lean, 1f);
            }
        }

        private void EmitTongue(Vector3 position, Vector3 rise, float scale)
        {
            var tongue = tongues[nextTongue];
            nextTongue = (nextTongue + 1) % tongues.Length;
            tongue.Position = position;
            tongue.Velocity = rise;
            tongue.Age = 0f;
            tongue.Life = Random.Range(TongueMinSeconds, TongueMaxSeconds);
            tongue.Size = radius * Random.Range(0.75f, 1.35f) * scale;
            tongue.Transform.position = position;
            tongue.Transform.localScale = Vector3.zero;
            tongue.Transform.gameObject.SetActive(true);
            tongues[(nextTongue + tongues.Length - 1) % tongues.Length] = tongue;
        }

        private void UpdateTongues(float dt)
        {
            for (var i = 0; i < tongues.Length; i++)
            {
                var tongue = tongues[i];
                if (tongue.Age >= tongue.Life)
                {
                    continue;
                }

                tongue.Age += dt;
                // The air slows it as it rises.
                tongue.Velocity *= Mathf.Exp(-dt * 2.5f);
                tongue.Position += tongue.Velocity * dt;
                var t = Mathf.Clamp01(tongue.Age / tongue.Life);
                // Flares up quickly, then thins and sinks away.
                var grow = t < 0.18f ? t / 0.18f : 1f - (t - 0.18f) / 0.82f;
                grow = Mathf.Max(0f, grow);
                tongue.Transform.position = tongue.Position;
                tongue.Transform.localScale = new Vector3(0.8f + 0.4f * grow, 1f, 1f) * tongue.Size * grow;
                // Redder as it dies.
                SetBlock(tongue.Renderers, lean * 0.6f - new Vector3(tongue.Velocity.x, 0f, tongue.Velocity.z) * 0.05f,
                    1f - t * 0.85f);
                if (tongue.Age >= tongue.Life)
                {
                    tongue.Transform.gameObject.SetActive(false);
                }

                tongues[i] = tongue;
            }
        }

        private void EmitSpark(Vector3 position, Vector3 velocity)
        {
            var spark = sparks[nextSpark];
            nextSpark = (nextSpark + 1) % sparks.Length;
            spark.Position = position;
            spark.Velocity = velocity;
            spark.Age = 0f;
            spark.Transform.position = position;
            spark.Transform.gameObject.SetActive(true);
            sparks[(nextSpark + sparks.Length - 1) % sparks.Length] = spark;
        }

        private void UpdateSparks(float dt)
        {
            for (var i = 0; i < sparks.Length; i++)
            {
                var spark = sparks[i];
                if (spark.Age >= SparkSeconds)
                {
                    continue;
                }

                spark.Age += dt;
                spark.Velocity += Vector3.down * 2f * dt;
                spark.Position += spark.Velocity * dt;
                var fade = Mathf.Max(0f, 1f - spark.Age / SparkSeconds);
                spark.Transform.SetPositionAndRotation(spark.Position,
                    Quaternion.LookRotation(spark.Velocity.sqrMagnitude > 1e-4f ? spark.Velocity : Vector3.up));
                spark.Transform.localScale = new Vector3(0.06f, 0.06f, 0.2f) * fade;
                if (spark.Age >= SparkSeconds)
                {
                    spark.Transform.gameObject.SetActive(false);
                }

                sparks[i] = spark;
            }
        }

        private void SetBlock(Renderer[] renderers, Vector3 bend, float heat)
        {
            foreach (var renderer in renderers)
            {
                // Keeps the flame's own seed (FlameVisual).
                renderer.GetPropertyBlock(block);
                block.SetVector(LeanId, bend);
                block.SetFloat(HeatId, heat);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
