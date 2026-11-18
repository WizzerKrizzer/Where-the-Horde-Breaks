using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The plain wall across the end of the road (user, 2026-11-17): a log palisade with a gate,
    /// nothing on it and no upgrades yet. Goblins stop at it and keep hitting it. A broken wall
    /// alone does not lose the level; the run is lost when a goblin walks through the breach
    /// (the level's lives, 1 on level 1). Greybox look; TEMPORARY numbers.
    /// </summary>
    public sealed class LevelWall : MonoBehaviour, ICombatTarget, IOrientedCombatTarget
    {
        // The wall of the map that is loaded now; GameSession sets it up at the start of every run.
        public static LevelWall Active { get; private set; }

        private const float Depth = 0.9f;
        private EnemyManager enemies;
        private float maxHealth = 20f;
        private float health = 20f;
        private float halfLength = 3f;
        private bool registered;
        // Bought (user, 2026-11-18): until then it is not there and the goblins walk on.
        private bool built = true;
        private float baseHealth = 20f;
        private Transform[] stakes;
        private Transform gate;
        private Transform healthFill;
        private Transform healthBar;

        public Vector3 Position => transform.position;
        public bool IsAlive => health > 0f;
        public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
        public float CombatRadius => Depth * 0.5f;
        public float BlockCapacity => 99999f;
        public float CurrentBlockedMass => 0f;
        public float CurrentHealth => health;
        public float MaximumHealth => maxHealth;
        public float Armor => 0f;
        public float PhysicalResistance => 0f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => 0f;
        public Vector3 CombatAxis => transform.right;
        public float CombatHalfLength => halfLength;
        public float CombatHalfDepth => Depth * 0.5f;

        /// <summary>Builds the wall across the road at `distance` metres along it.</summary>
        public static LevelWall Create(Transform parent, PathRoute route, float distance, float wallHealth, float roadWidth)
        {
            var point = route.Sample(distance);
            var ahead = route.Sample(Mathf.Min(route.TotalLength, distance + 0.5f)) - point;
            ahead.y = 0f;
            var go = new GameObject("LevelWall");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(point.x, 0f, point.z);
            go.transform.rotation = Quaternion.LookRotation(ahead.sqrMagnitude > 0.0001f ? ahead.normalized : Vector3.right, Vector3.up);
            var wall = go.AddComponent<LevelWall>();
            // A little wider than the road, so no goblin slips past its ends.
            wall.halfLength = roadWidth * 0.5f + 0.6f;
            wall.maxHealth = Mathf.Max(1f, wallHealth);
            wall.baseHealth = wall.maxHealth;
            wall.health = wall.maxHealth;
            wall.BuildVisual();
            Active = wall;
            return wall;
        }

        /// <summary>Whether it is bought, and the health its upgrades add to the level's own.</summary>
        public void Configure(bool isBuilt, float extraHealth)
        {
            built = isBuilt;
            maxHealth = Mathf.Max(1f, baseHealth + Mathf.Max(0f, extraHealth));
            health = maxHealth;
            gameObject.SetActive(built);
            if (built)
            {
                UpdateVisual();
            }
        }

        public bool IsBuilt => built;

        /// <summary>A new run: whole again, and a target for the horde (when it is bought).</summary>
        public void BeginRun(EnemyManager enemyManager)
        {
            if (registered && enemies != null)
            {
                enemies.UnregisterCombatTarget(this);
                registered = false;
            }

            if (!built)
            {
                return;
            }

            enemies = enemyManager;
            health = maxHealth;
            UpdateVisual();
            enemies?.RegisterCombatTarget(this);
            registered = enemies != null;
        }

        public bool TryAddBlocker(EnemyActor enemy) => IsAlive;
        public void RemoveBlocker(EnemyActor enemy) { }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (!IsAlive)
            {
                return;
            }

            health = Mathf.Max(0f, health - Mathf.Max(0f, damage));
            UpdateVisual();
            if (health <= 0f)
            {
                Break();
            }
        }

        public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition)
        {
            health = Mathf.Clamp(authoritativeHealth, 0f, maxHealth);
            if (destroyed)
            {
                health = 0f;
            }

            UpdateVisual();
            if (health <= 0f)
            {
                Break();
            }
        }

        private void Break()
        {
            if (registered)
            {
                enemies?.UnregisterCombatTarget(this);
                registered = false;
            }
        }

        private void OnDestroy()
        {
            if (registered)
            {
                enemies?.UnregisterCombatTarget(this);
            }

            if (Active == this)
            {
                Active = null;
            }
        }

        private void BuildVisual()
        {
            var wood = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.46f));
            var dark = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.3f));
            var count = Mathf.Max(6, Mathf.RoundToInt(halfLength * 2f / 0.42f));
            stakes = new Transform[count];
            var random = new System.Random(17);
            for (var i = 0; i < count; i++)
            {
                var x = -halfLength + (i + 0.5f) * (halfLength * 2f / count);
                var height = 2.1f + (float)random.NextDouble() * 0.5f;
                var stake = new GameObject($"Stake_{i}").transform;
                stake.SetParent(transform, false);
                stake.localPosition = new Vector3(x, 0f, 0f);
                stake.localRotation = Quaternion.Euler((float)random.NextDouble() * 6f - 3f, 0f, (float)random.NextDouble() * 6f - 3f);
                var log = Part(PrimitiveType.Cylinder, stake, wood, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.38f, height * 0.5f, 0.38f));
                log.name = "Log";
                // The sharpened top: a squashed cube turned on its corner.
                Part(PrimitiveType.Cube, stake, wood, new Vector3(0f, height + 0.1f, 0f), new Vector3(0.24f, 0.32f, 0.24f), Quaternion.Euler(45f, 0f, 45f));
                stakes[i] = stake;
            }

            // Two cross beams on the outside, and the gate: planks in the middle of the road.
            Part(PrimitiveType.Cube, transform, dark, new Vector3(0f, 0.7f, -0.26f), new Vector3(halfLength * 2f, 0.16f, 0.12f));
            Part(PrimitiveType.Cube, transform, dark, new Vector3(0f, 1.6f, -0.26f), new Vector3(halfLength * 2f, 0.16f, 0.12f));
            gate = new GameObject("Gate").transform;
            gate.SetParent(transform, false);
            Part(PrimitiveType.Cube, gate, dark, new Vector3(0f, 1.05f, -0.34f), new Vector3(2.2f, 2.0f, 0.1f));
            Part(PrimitiveType.Cube, gate, wood, new Vector3(0f, 1.05f, -0.4f), new Vector3(0.12f, 2.0f, 0.06f));

            healthBar = new GameObject("WallHealth").transform;
            healthBar.SetParent(transform, false);
            healthBar.localPosition = new Vector3(0f, 3.3f, 0f);
            Part(PrimitiveType.Cube, healthBar, BootstrapMaterials.GetUnlit(PlaceholderPalette.Dark), Vector3.zero, new Vector3(3.2f, 0.12f, 0.36f));
            healthFill = Part(PrimitiveType.Cube, healthBar, BootstrapMaterials.GetUnlit(PlaceholderPalette.Accent), new Vector3(0f, 0.02f, 0f), new Vector3(3.1f, 0.12f, 0.28f)).transform;
            UpdateVisual();
        }

        private void LateUpdate()
        {
            // The bar faces the camera's view direction, so it reads from any zoom.
            var camera = Camera.main;
            if (healthBar != null && camera != null)
            {
                healthBar.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized, Vector3.up);
            }
        }

        private void UpdateVisual()
        {
            if (stakes == null)
            {
                return;
            }

            var fraction = Mathf.Clamp01(health / Mathf.Max(1f, maxHealth));
            // More broken each quarter of its health: stakes fall away from the middle out,
            // the gate goes at half, and at 0 only stumps stay.
            var standing = Mathf.CeilToInt(stakes.Length * Mathf.Ceil(fraction * 4f) / 4f);
            for (var i = 0; i < stakes.Length; i++)
            {
                var fromMiddle = Mathf.Abs(i - (stakes.Length - 1) * 0.5f);
                var rank = stakes.Length - 1 - Mathf.RoundToInt(fromMiddle * 2f);
                var up = rank < standing;
                stakes[i].localScale = up ? Vector3.one : new Vector3(1f, 0.18f, 1f);
            }

            if (gate != null)
            {
                gate.gameObject.SetActive(fraction > 0.5f);
            }

            if (healthFill != null)
            {
                healthFill.localScale = new Vector3(3.1f * fraction, 0.12f, 0.28f);
                healthFill.localPosition = new Vector3(-1.55f * (1f - fraction), 0.02f, 0f);
                healthBar.gameObject.SetActive(fraction > 0f);
            }
        }

        private static GameObject Part(PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale, Quaternion? rotation = null)
        {
            var part = GameObject.CreatePrimitive(type);
            foreach (var component in part.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }

            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation ?? Quaternion.identity;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
    }
}
